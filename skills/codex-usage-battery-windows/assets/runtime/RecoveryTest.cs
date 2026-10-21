using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CodexBattery;

internal static class RecoveryTest
{
    static List<string> checks=new List<string>();
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
    static bool Gone(int? id){if(!id.HasValue)return true;try{using(var p=Process.GetProcessById(id.Value))return p.HasExited;}catch(ArgumentException){return true;}}
    [STAThread] static void Main()
    {
        Application.EnableVisualStyles();
        string folder=AppDomain.CurrentDomain.BaseDirectory,failure=null;
        var connections=new List<QuotaConnection>();
        var context=new ApplicationContext();
        var host=new Form {Text="Recovery test host",Bounds=new Rectangle(120,150,900,400)};
        host.Show();Native.ShowWindow(host.Handle,4);
        var battery=new BatteryForm(null,false) {TestMode=true,TestHostProcess=true,TestTarget=host.Handle};
        battery.TestConnectionFactory=delegate {
            var c=new QuotaConnection(Path.Combine(folder,"codex.exe"),"app-server normal",150,1000,100,500);
            connections.Add(c);c.Start();return c;
        };
        var timer=new Timer {Interval=100};int phase=0;DateTime entered=DateTime.UtcNow,limit=entered.AddSeconds(25);int? pid=null;
        battery.StartTracking(true);
        timer.Tick+=delegate {
            try {
                if(DateTime.UtcNow>limit)throw new Exception("recovery regression timed out in phase "+phase);
                if(phase==0) {
                    if(connections.Count==0 || connections[0].ReadState().Reads<2)return;
                    var s=connections[0].ReadState();pid=s.ProcessId;
                    Check(battery.Visible,"initial window displays battery");
                    Check(s.ProcessStarts==1,"normal polling reuses query child");
                    host.Hide();entered=DateTime.UtcNow;phase=1;
                } else if(phase==1) {
                    if((DateTime.UtcNow-entered).TotalSeconds<2)return;
                    Check(!battery.IsDisposed && !battery.Visible,"hidden window retains invisible monitor beyond old exit timeout");
                    Check(connections[0].ReadState().Stopped && Gone(pid),"hidden window stops worker and removes query child");
                    Check(connections.Count==1,"monitor does not restart query while no window exists");
                    host.Show();Native.ShowWindow(host.Handle,4);phase=2;
                } else if(phase==2) {
                    if(connections.Count<2 || connections[1].ReadState().Reads<2)return;
                    var s=connections[1].ReadState();pid=s.ProcessId;
                    Check(battery.Visible,"same window restoration automatically shows battery");
                    Check(connections.Count==2 && s.ProcessStarts==1,"restoration starts exactly one new query connection");
                    host.Close();host.Dispose();battery.TestTarget=IntPtr.Zero;entered=DateTime.UtcNow;phase=3;
                } else if(phase==3) {
                    if((DateTime.UtcNow-entered).TotalSeconds<2)return;
                    Check(!battery.IsDisposed && !battery.Visible,"destroying host window does not destroy monitor");
                    Check(connections[1].ReadState().Stopped && Gone(pid),"destroyed window also cleans its query child");
                    host=new Form {Text="Replacement recovery host",Bounds=new Rectangle(150,170,900,400)};
                    host.Show();Native.ShowWindow(host.Handle,4);battery.TestTarget=host.Handle;phase=4;
                } else if(phase==4) {
                    if(connections.Count<3 || connections[2].ReadState().Reads<2)return;
                    pid=connections[2].ReadState().ProcessId;
                    Check(battery.Visible && !battery.IsDisposed,"new window reattaches without process-start event");
                    Check(connections.Count==3,"recreated window starts one connection only");
                    host.Close();host.Dispose();host=null;battery.TestTarget=IntPtr.Zero;battery.TestHostProcess=false;phase=5;
                } else {
                    if(!battery.IsDisposed)return;
                    Check(Gone(pid),"GUI process exit removes last query child");
                    Check(connections[2].ReadState().Stopped,"GUI process exit stops final worker");
                    timer.Stop();context.ExitThread();
                }
            }catch(Exception e){failure=e.Message;timer.Stop();context.ExitThread();}
        };
        timer.Start();Application.Run(context);timer.Dispose();
        if(!battery.IsDisposed)battery.Close();if(host!=null)host.Dispose();foreach(var c in connections)c.Dispose();
        File.WriteAllText(Path.Combine(folder,"recovery-test.json"),new JavaScriptSerializer().Serialize(new {ok=failure==null,count=checks.Count,checks=checks,failure=failure}));
        if(failure!=null)Environment.ExitCode=1;
    }
}
