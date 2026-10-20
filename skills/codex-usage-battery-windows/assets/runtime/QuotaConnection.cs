using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace CodexBattery
{
    internal sealed class QuotaState
    {
        internal UsageSnapshot Snapshot;
        internal bool Connected, Busy, Stopped;
        internal DateTime? NextUpdateUtc;
        internal int? ProcessId;
        internal int ProcessStarts, Reads, Notifications, Failures, RetryMilliseconds;
        internal string Error, Source;
    }

    // One worker owns all protocol reads and writes. Only this worker can launch a child.
    internal sealed class QuotaConnection : IDisposable
    {
        private readonly object gate=new object();
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private readonly string executable,arguments;
        private readonly int interval,timeout,retryBase,retryMax;
        private readonly QuotaState state=new QuotaState();
        private Process child;
        private Task worker;
        private bool stopping,refresh=true;
        internal QuotaConnection(string exe,string args="app-server",int pollMilliseconds=30000,
            int timeoutMilliseconds=20000,int retryBaseMilliseconds=5000,int retryMaxMilliseconds=60000)
        {executable=exe;arguments=args;interval=pollMilliseconds;timeout=timeoutMilliseconds;retryBase=retryBaseMilliseconds;retryMax=retryMaxMilliseconds;}

        internal QuotaState ReadState()
        {
            lock(gate) return new QuotaState {Snapshot=state.Snapshot,Connected=state.Connected,Busy=state.Busy,
                Stopped=state.Stopped,NextUpdateUtc=state.NextUpdateUtc,ProcessId=state.ProcessId,
                ProcessStarts=state.ProcessStarts,Reads=state.Reads,Notifications=state.Notifications,
                Failures=state.Failures,RetryMilliseconds=state.RetryMilliseconds,Error=state.Error,Source=state.Source};
        }
        internal void Start() {lock(gate) {if(stopping)throw new ObjectDisposedException("QuotaConnection");if(worker==null)worker=Task.Run((Action)Run);}}
        internal void RequestRefresh() {lock(gate) {if(stopping || state.Busy)return;refresh=true;wake.Set();}}
        private bool IsStopping {get {lock(gate)return stopping;}}
        private int Fail(string message,bool disconnect)
        {
            lock(gate) {
                state.Failures++;
                state.RetryMilliseconds=(int)Math.Min(retryMax,retryBase*Math.Pow(2,Math.Min(10,state.Failures-1)));
                state.Error=message;state.Busy=false;
                if(disconnect){state.Connected=false;state.ProcessId=null;}
                state.NextUpdateUtc=DateTime.UtcNow.AddMilliseconds(state.RetryMilliseconds);
                return state.RetryMilliseconds;
            }
        }
        private void Publish(UsageSnapshot snapshot,string source,bool fullRead)
        {
            lock(gate) {
                state.Snapshot=snapshot;state.Source=source;
                if(fullRead)state.Reads++;else state.Notifications++;
                if(fullRead){state.Error=null;state.Failures=0;state.RetryMilliseconds=0;}
            }
        }
        private static Task<string> NextLine(Process p)
        {
            var read=p.StandardOutput.ReadLineAsync();
            read.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            return read;
        }
        private static void CloseChild(Process p)
        {if(p==null)return;try {if(!p.HasExited){p.Kill();p.WaitForExit(1500);}}catch{}try{p.Dispose();}catch{}}
        private bool WaitUntil(DateTime next)
        {
            while(!IsStopping && DateTime.UtcNow<next)wake.WaitOne((int)Math.Max(1,Math.Min(100,(next-DateTime.UtcNow).TotalMilliseconds)));
            return !IsStopping;
        }
        private void Run()
        {
            while(!IsStopping) {
                Process process=null;
                try {
                    lock(gate) {
                        if(stopping)break;
                        process=Process.Start(new ProcessStartInfo(executable,arguments) {UseShellExecute=false,
                            CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                            WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory});
                        child=process;state.ProcessStarts++;state.ProcessId=process.Id;state.Busy=true;refresh=false;
                    }
                    process.ErrorDataReceived+=delegate{};process.BeginErrorReadLine();
                    process.StandardInput.AutoFlush=true;
                    int serial=1,generation=0,pendingGeneration=0;
                    string pending="1";
                    DateTime deadline=DateTime.UtcNow.AddMilliseconds(timeout),nextRead=DateTime.UtcNow,retryNotBefore=DateTime.MinValue;
                    lock(gate)state.NextUpdateUtc=deadline;
                    process.StandardInput.WriteLine("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_usage_battery\",\"version\":\"1.1.1\"}}}");
                    Task<string> line=NextLine(process);
                    var serializer=new JavaScriptSerializer();
                    while(!IsStopping) {
                        if(line.IsCompleted) {
                            string text=line.GetAwaiter().GetResult();
                            if(text==null)throw new IOException("disconnected");
                            var reply=UsageSnapshot.Map(serializer.DeserializeObject(text));
                            if(reply==null)throw new IOException("invalid protocol");
                            string method=Convert.ToString(UsageSnapshot.Get(reply,"method"));
                            if(method=="account/rateLimits/updated") {
                                UsageSnapshot previous=ReadState().Snapshot;
                                var merged=UsageSnapshot.MergeNotification(text,previous);
                                if(merged!=previous && merged!=null)Publish(merged,"notification",false);
                            } else if(method=="account/updated") {
                                generation++;
                                lock(gate){state.Snapshot=null;state.Source=null;state.Error="登录状态变化，正在校对";}
                                nextRead=DateTime.UtcNow;
                            } else if(pending!=null && Convert.ToString(UsageSnapshot.Get(reply,"id"))==pending) {
                                bool initializing=pending=="1";
                                pending=null;
                                if(!initializing && pendingGeneration!=generation) {
                                    // Drain the old request before sending another; never publish an earlier account's reply.
                                    lock(gate)state.Busy=false;
                                    nextRead=DateTime.UtcNow;
                                } else if(UsageSnapshot.Get(reply,"error")!=null) {
                                    if(initializing)throw new IOException("initialization failed");
                                    int delay=Fail("额度查询失败，将自动重试",false);
                                    nextRead=DateTime.UtcNow.AddMilliseconds(delay);
                                    retryNotBefore=nextRead;
                                    // Do not retain another account's figures after an authorization failure.
                                    var error=UsageSnapshot.Map(UsageSnapshot.Get(reply,"error"));
                                    object code=UsageSnapshot.Get(error,"code");
                                    if(Convert.ToString(code)=="401" || Convert.ToString(code)=="403")lock(gate)state.Snapshot=null;
                                } else if(initializing) {
                                    process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                                    lock(gate){state.Connected=true;state.Busy=false;}
                                    nextRead=DateTime.UtcNow;
                                } else {
                                    object result=UsageSnapshot.Get(reply,"result");
                                    if(result==null)throw new IOException("missing quota result");
                                    Publish(UsageSnapshot.Parse(serializer.Serialize(result)),"read",true);
                                    nextRead=DateTime.UtcNow.AddMilliseconds(interval);
                                    retryNotBefore=DateTime.MinValue;
                                    lock(gate){state.Busy=false;state.NextUpdateUtc=nextRead;}
                                }
                            }
                            line=NextLine(process);
                        }
                        if(process.HasExited)throw new IOException("disconnected");
                        bool requested;
                        lock(gate){requested=refresh;}
                        if(pending==null && DateTime.UtcNow>=retryNotBefore && (requested || DateTime.UtcNow>=nextRead)) {
                            lock(gate)refresh=false;
                            pending=(++serial).ToString();deadline=DateTime.UtcNow.AddMilliseconds(timeout);
                            pendingGeneration=generation;
                            lock(gate){state.Busy=true;state.NextUpdateUtc=deadline;}
                            process.StandardInput.WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":"+pending+"}");
                        }
                        if(pending!=null && DateTime.UtcNow>=deadline)throw new TimeoutException();
                        wake.WaitOne(50);
                    }
                } catch(Exception e) {
                    if(!IsStopping)Fail(e is TimeoutException ? "连接超时，将自动重连" : "连接中断，将自动重连",true);
                } finally {
                    lock(gate){if(child==process)child=null;state.Connected=false;state.Busy=false;state.ProcessId=null;}
                    CloseChild(process);
                }
                if(!IsStopping)WaitUntil(ReadState().NextUpdateUtc ?? DateTime.UtcNow.AddMilliseconds(retryBase));
            }
            lock(gate){state.Stopped=true;state.Connected=false;state.Busy=false;state.ProcessId=null;state.NextUpdateUtc=null;}
        }
        public void Dispose()
        {
            Process process;Task task;
            lock(gate){if(stopping)return;stopping=true;process=child;task=worker;}
            wake.Set();CloseChild(process);
            if(task!=null) {
                bool complete=false;try{complete=task.Wait(2000);}catch{complete=true;}
                if(complete)wake.Dispose();else task.ContinueWith(t=>wake.Dispose());
            }else wake.Dispose();
        }
    }

    internal static class QuotaClient
    {
        private static readonly object gate=new object();
        private static QuotaConnection shared;
        internal static string FindCodex()
        {
            string pathFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codex-executable.txt");
            if(File.Exists(pathFile)) {
                string configured=File.ReadAllText(pathFile).Trim();
                if(Path.IsPathRooted(configured) && File.Exists(configured) && String.Equals(Path.GetFileName(configured),"codex.exe",StringComparison.OrdinalIgnoreCase))return configured;
                throw new InvalidOperationException("Configured desktop Codex executable is unavailable");
            }
            foreach(Process p in Process.GetProcessesByName("codex"))using(p)try {
                string path=p.MainModule.FileName;
                if(path.IndexOf("OpenAI\\Codex\\bin",StringComparison.OrdinalIgnoreCase)>=0 && File.Exists(path))return path;
            }catch{}
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if(Directory.Exists(root)) {
                string result=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(result!=null)return result;
            }
            throw new InvalidOperationException("找不到桌面版 Codex 配套程序");
        }
        internal static QuotaConnection Connect()
        {lock(gate){if(shared==null){shared=new QuotaConnection(FindCodex());shared.Start();}return shared;}}
        internal static UsageSnapshot Fetch()
        {
            var connection=Connect();int reads=connection.ReadState().Reads;connection.RequestRefresh();
            DateTime deadline=DateTime.UtcNow.AddSeconds(25);
            while(DateTime.UtcNow<deadline) {
                var status=connection.ReadState();
                if(status.Reads>reads && status.Snapshot!=null)return status.Snapshot;
                if(status.Error!=null)throw new InvalidOperationException("读取失败，请检查网络与登录");
                Thread.Sleep(25);
            }
            throw new TimeoutException("查询用量超时");
        }
        internal static void Shutdown()
        {QuotaConnection connection;lock(gate){connection=shared;shared=null;}if(connection!=null)connection.Dispose();}
    }
}
