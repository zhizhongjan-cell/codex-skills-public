using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using CodexBattery;

internal static class ConnectionTests
{
    private static readonly List<string> checks=new List<string>();
    private static string exe;
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
    private static bool Until(Func<bool> condition,int timeout=4000)
    {DateTime end=DateTime.UtcNow.AddMilliseconds(timeout);while(DateTime.UtcNow<end){if(condition())return true;Thread.Sleep(10);}return condition();}
    private static bool Alive(int id){try{using(var p=Process.GetProcessById(id))return !p.HasExited;}catch{return false;}}
    private static QuotaConnection New(string mode,int poll=800,int timeout=600)
    {var c=new QuotaConnection(exe,"app-server "+mode,poll,timeout,150,600);c.Start();return c;}
    public static void Main()
    {
        exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codex.exe");
        string failure=null;
        try {
            var first=UsageSnapshot.Parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":10080}}}");
            var partial=UsageSnapshot.MergeNotification("{\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":31,\"windowDurationMins\":300}}}}",first);
            Check(partial.PrimaryRemaining==69 && partial.WeeklyRemaining==80,"partial notification preserves weekly window");
            var fields=UsageSnapshot.MergeNotification("{\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"secondary\":{\"usedPercent\":21}}}}",partial);
            Check(fields.WeeklyRemaining==79 && fields.SecondaryMinutes==10080,"partial fields preserve weekly duration");
            var resetOnly=UsageSnapshot.MergeNotification("{\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"secondary\":{\"resetsAt\":1800500000}}}}",fields);
            Check(resetOnly.WeeklyRemaining==79 && resetOnly.SecondaryFetchedUtc==fields.SecondaryFetchedUtc,"reset-only update does not erase quota or refresh its timestamp");
            Check(partial.SecondaryFetchedUtc==first.SecondaryFetchedUtc,"primary notification does not refresh weekly timestamp");
            Check(Object.ReferenceEquals(partial,UsageSnapshot.MergeNotification("{\"params\":{\"rateLimits\":{\"limitId\":\"premium\",\"primary\":null}}}",partial)),"unrelated bucket cannot overwrite or refresh quota");
            var cleared=UsageSnapshot.MergeNotification("{\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"secondary\":null}}}",partial);
            Check(cleared.PrimaryRemaining==69 && cleared.WeeklyRemaining==null,"explicit null clears weekly without clearing primary");
            Check(UsageSnapshot.Parse("{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"usedPercent\":2}}}").PrimaryRemaining==null,"tagged other single bucket rejected");
            Check(UsageSnapshot.Parse("{\"rateLimits\":{\"secondary\":{\"usedPercent\":2,\"windowDurationMins\":600}}}").WeeklyRemaining==null,"non-weekly window is not mislabeled as weekly");
            int child;
            using(var c=New("notify")) {
                Check(Until(()=>c.ReadState().Notifications==1),"unsolicited official-method notification consumed without polling");
                var s=c.ReadState();child=s.ProcessId.Value;
                Check(s.Snapshot.PrimaryRemaining==69 && s.Snapshot.WeeklyRemaining==80,"notification updates primary and retains weekly");
                Check(s.NextUpdateUtc.HasValue && s.NextUpdateUtc.Value>DateTime.UtcNow,"next scheduled check exposed for clock");
                c.RequestRefresh();Check(Until(()=>c.ReadState().Reads>=2),"manual refresh receives fresh reply");
                Check(c.ReadState().ProcessStarts==1 && c.ReadState().ProcessId==child,"notification and manual refresh retain same process");
            }
            Check(!Alive(child),"disposing healthy connection removes its query child");
            using(var c=New("normal",150)) {
                Check(Until(()=>c.ReadState().Reads>=3),"scheduled checks continue through persistent connection");
                Check(c.ReadState().ProcessStarts==1,"three scheduled checks start one process");
            }
            using(var c=New("error")) {
                Check(Until(()=>c.ReadState().Failures>=2),"RPC failures trigger retry backoff");
                var s=c.ReadState();Check(s.Connected && s.ProcessStarts==1 && s.RetryMilliseconds>=300,"RPC error keeps connection instead of restarting process");
                Check(s.Snapshot==null && s.Error!=null,"failed initial quota remains unknown");
                int failures=s.Failures;
                DateTime limit=DateTime.UtcNow.AddMilliseconds(80);
                while(DateTime.UtcNow<limit){c.RequestRefresh();Thread.Sleep(5);}
                Check(c.ReadState().Failures==failures,"repeated refresh requests cannot bypass error backoff");
            }
            using(var c=New("error-notify")) {
                Check(Until(()=>c.ReadState().Failures>=2 && c.ReadState().Notifications>=1),"partial pushes do not reset failing-read backoff");
                Check(c.ReadState().Error!=null && c.ReadState().RetryMilliseconds>=300,"push preserves query failure warning and retry schedule");
            }
            using(var c=New("auth-inflight")) {
                Check(Until(()=>c.ReadState().Snapshot!=null && c.ReadState().Snapshot.PrimaryRemaining==45),"new account fetched after old in-flight reply drained");
                Check(c.ReadState().Reads==1,"old account successful response is never published");
            }
            using(var c=New("disconnect")) {
                Check(Until(()=>c.ReadState().ProcessStarts>=2),"closed transport reconnects after backoff");
            }
            using(var c=New("hang",800,180)) {
                Check(Until(()=>c.ReadState().ProcessStarts>=2),"initialization timeout reconnects without permanent waiting");
                child=c.ReadState().ProcessId ?? -1;
            }
            Check(child<0 || !Alive(child),"timed-out connection leaves no active recorded child");
            using(var c=New("auth")) {
                Check(Until(()=>c.ReadState().Reads>=1),"quota available before simulated logout");
                Check(Until(()=>c.ReadState().Snapshot==null && c.ReadState().Error!=null),"account change clears previous account quota");
                Check(c.ReadState().ProcessStarts==1,"auth error does not loop process restarts");
            }
            var pending=New("read-hang",800,5000);
            Check(Until(()=>pending.ReadState().Connected && pending.ReadState().Busy),"pending read established for shutdown test");
            child=pending.ReadState().ProcessId.Value;
            var clock=Stopwatch.StartNew();pending.Dispose();clock.Stop();
            Check(clock.ElapsedMilliseconds<2500 && pending.ReadState().Stopped && !Alive(child),"shutdown cancels pending read and exits worker promptly");
            using(var panel=new BatteryForm(null,false){TestMode=true})Check(panel.Width>=230 && panel.Height>=32,"panel reserves space for battery clock and weekly text");
        }catch(Exception e){failure=e.Message;Console.WriteLine("FAIL "+failure);Environment.ExitCode=1;}
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"connection-tests.json"),new JavaScriptSerializer().Serialize(new {ok=failure==null,count=checks.Count,checks=checks,failure=failure}));
    }
}
