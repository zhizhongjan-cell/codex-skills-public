using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexBattery
{
    internal static class ColdStartTest
    {
        private delegate bool Callback(IntPtr h,IntPtr p);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Callback c,IntPtr p);
        [STAThread] private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            string folder=AppDomain.CurrentDomain.BaseDirectory;
            string hostFile=Path.Combine(folder,"cold-test-host.txt");
            if(args.Length==2 && args[0]=="--cold-child") {
                int parentId=Int32.Parse(args[1]);
                var context=new ApplicationContext();
                using(var form=new BatteryForm(null,false) {TestMode=true,LaunchRequested=true})
                using(var updater=new Timer {Interval=100}) {
                    FieldInfo processFlag=typeof(BatteryForm).GetField("TestHostProcess",BindingFlags.NonPublic|BindingFlags.Instance);
                    Action update=delegate {
                        bool alive;
                        try {using(var parent=Process.GetProcessById(parentId)) alive=!parent.HasExited;}catch {alive=false;}
                        if(processFlag!=null) processFlag.SetValue(form,alive);
                        try {form.TestTarget=new IntPtr(Int64.Parse(File.ReadAllText(hostFile)));}catch {form.TestTarget=IntPtr.Zero;}
                    };
                    form.FormClosed+=delegate {context.ExitThread();};
                    update(); updater.Tick+=delegate {update();};updater.Start();
                    form.StartTracking(false);if(!form.IsDisposed)Application.Run(context);
                }
                return;
            }
            var checks=new List<string>();string failure=null;
            Form host=null;Process child=null;
            var appContext=new ApplicationContext();
            var timer=new Timer {Interval=100};int phase=0;
            DateTime started=DateTime.UtcNow,deadline=started.AddSeconds(40);
            File.WriteAllText(hostFile,"0");
            child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--cold-child "+Process.GetCurrentProcess().Id) {UseShellExecute=false,CreateNoWindow=true});
            timer.Tick+=delegate {
                try {
                    if(phase==0) {
                        if(child.HasExited)throw new Exception("battery exited before delayed GUI: "+Math.Round((DateTime.UtcNow-started).TotalSeconds,1)+" seconds");
                        if((DateTime.UtcNow-started).TotalSeconds<32)return;
                        if(Visible(child.Id))throw new Exception("battery visible before host GUI");
                        checks.Add("battery survives 32-second window delay while host process is alive");
                        host=new Form {Text="Cold-start regression host",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(120,120,900,400)};
                        host.Show();Native.ShowWindow(host.Handle,4);
                        File.WriteAllText(hostFile,host.Handle.ToInt64().ToString());deadline=DateTime.UtcNow.AddSeconds(5);phase=1;
                    } else if(phase==1) {
                        if(!Visible(child.Id)){if(child.HasExited || DateTime.UtcNow>deadline)throw new Exception("battery did not appear when delayed GUI became ready");return;}
                        checks.Add("battery appears when delayed GUI is ready");
                        host.Close();host.Dispose();host=null;File.WriteAllText(hostFile,"0");deadline=DateTime.UtcNow.AddSeconds(5);phase=2;
                    } else {
                        if(!child.HasExited){if(DateTime.UtcNow>deadline)throw new Exception("battery remained after GUI closed");return;}
                        if(child.ExitCode!=0)throw new Exception("battery child crashed");
                        checks.Add("battery exits after GUI close even while host parent process remains alive");
                        timer.Stop();appContext.ExitThread();
                    }
                }catch(Exception e){failure=e.Message;timer.Stop();appContext.ExitThread();}
            };
            timer.Start();Application.Run(appContext);timer.Dispose();
            if(host!=null){host.Close();host.Dispose();}
            if(child!=null){if(!child.HasExited){child.Kill();child.WaitForExit(2000);}child.Dispose();}
            File.WriteAllText(Path.Combine(folder,"cold-start-test.json"),new JavaScriptSerializer().Serialize(new {ok=failure==null,checks=checks,failure=failure,elapsedSeconds=Math.Round((DateTime.UtcNow-started).TotalSeconds,1)}));
            if(failure!=null)Environment.ExitCode=1;
        }
        private static bool Visible(int id)
        {
            bool found=false;
            EnumWindows(delegate(IntPtr h,IntPtr p){uint pid;Native.GetWindowThreadProcessId(h,out pid);if(pid==id && Native.IsWindowVisible(h)){found=true;return false;}return true;},IntPtr.Zero);
            return found;
        }
    }
}
