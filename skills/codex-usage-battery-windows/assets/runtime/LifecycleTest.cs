using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexBattery
{
    internal static class LifecycleTest
    {
        private delegate bool WindowCallback(IntPtr hwnd,IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr state);
        [STAThread] private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            if(args.Length==2 && args[0]=="--child") {
                var childContext=new ApplicationContext();
                using(var childForm=new BatteryForm(null,false) {TestMode=true,TestTarget=new IntPtr(Int64.Parse(args[1]))}) {
                    childForm.FormClosed+=delegate {childContext.ExitThread();};
                    childForm.StartTracking(false);
                    if(!childForm.IsDisposed) Application.Run(childContext);
                }
                return;
            }
            var context=new ApplicationContext();
            var checks=new List<string>();
            string failure=null;
            Form host=null;
            Process child=null;
            var children=new List<Process>();
            var timer=new Timer { Interval=350 };
            int step=0;
            DateTime deadline=DateTime.UtcNow.AddSeconds(5);
            child=StartChild(IntPtr.Zero); children.Add(child);
            timer.Tick += delegate {
                try {
                    if(step==0) {
                        if(!child.HasExited) {if(DateTime.UtcNow>deadline) throw new Exception("no-host battery failed to exit"); return;}
                        if(child.ExitCode!=0) throw new Exception("no-host battery exit failed");
                        checks.Add("no host means no waiting battery process");
                        host=NewHost(); child=StartChild(host.Handle);children.Add(child);deadline=DateTime.UtcNow.AddSeconds(5);
                    } else if(step==1) {
                        if(!VisibleChild(child)) {if(child.HasExited || DateTime.UtcNow>deadline) throw new Exception("host open must show battery");return;}
                        checks.Add("desktop host launch shows battery process");
                        host.WindowState=FormWindowState.Minimized;
                    } else if(step==2) {
                        if(child.HasExited || VisibleChild(child)) throw new Exception("minimize must hide but keep battery alive");
                        checks.Add("minimize hides battery without exiting");
                        host.WindowState=FormWindowState.Normal;
                    } else if(step==3) {
                        if(!VisibleChild(child)) throw new Exception("restore must show battery");
                        checks.Add("restore shows battery");
                        host.Close();host.Dispose();host=null;deadline=DateTime.UtcNow.AddSeconds(5);
                    } else if(step==4) {
                        if(!child.HasExited) {if(DateTime.UtcNow>deadline) throw new Exception("host close left battery running");return;}
                        if(child.ExitCode!=0) throw new Exception("host close battery exit failed");
                        checks.Add("closing last host exits battery process");
                        host=NewHost();child=StartChild(host.Handle);children.Add(child);deadline=DateTime.UtcNow.AddSeconds(5);
                    } else if(step==5) {
                        if(!VisibleChild(child)) {if(child.HasExited || DateTime.UtcNow>deadline) throw new Exception("new host must launch fresh battery");return;}
                        checks.Add("next host launch starts a new battery process");
                        host.Close();host.Dispose();host=null;deadline=DateTime.UtcNow.AddSeconds(5);
                    } else if(step==6) {
                        if(!child.HasExited) {if(DateTime.UtcNow>deadline) throw new Exception("second close left battery running");return;}
                        checks.Add("no child battery remains after second close");
                        timer.Stop(); context.ExitThread();
                    }
                    step++;
                } catch(Exception e) { failure=e.GetType().Name+": "+e.Message; timer.Stop(); context.ExitThread(); }
            };
            timer.Start(); Application.Run(context);
            timer.Dispose();
            if(host!=null) { host.Close(); host.Dispose(); }
            foreach(Process p in children) {try {if(!p.HasExited){p.Kill();p.WaitForExit(2000);}}catch{}p.Dispose();}
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lifecycle-test.json"),
                new JavaScriptSerializer().Serialize(new {ok=failure==null,checks=checks,failure=failure}));
            if(failure!=null) Environment.ExitCode=1;
        }
        private static Process StartChild(IntPtr handle)
        { return Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--child "+handle.ToInt64()) {UseShellExecute=false,CreateNoWindow=true}); }
        private static bool VisibleChild(Process child)
        {
            if(child.HasExited)return false;
            bool visible=false;
            EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {uint pid;Native.GetWindowThreadProcessId(hwnd,out pid);
                if(pid==child.Id && Native.IsWindowVisible(hwnd)) {visible=true;return false;}return true;},IntPtr.Zero);
            return visible;
        }
        private static Form NewHost()
        {
            var host=new Form {Text="Battery lifecycle test",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(120,120,900,400)};
            host.Show(); Native.ShowWindow(host.Handle,4); return host;
        }
    }
}
