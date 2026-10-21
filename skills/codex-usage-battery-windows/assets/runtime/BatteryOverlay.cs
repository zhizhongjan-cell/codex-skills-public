using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexBattery
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        internal static void SetOverlayLayer(IntPtr hwnd,bool raised)
        { SetWindowPos(hwnd,new IntPtr(raised ? -1 : -2),0,0,0,0,0x1|0x2|0x10|0x200); }
        private delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr state);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint="SetWindowLong")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        internal static void SetOwner(IntPtr hwnd, IntPtr owner) {
            if (IntPtr.Size == 8) SetWindowLongPtr(hwnd, -8, owner); else SetWindowLong32(hwnd, -8, owner.ToInt32());
        }
        internal static float Scale(IntPtr hwnd) { try { return Math.Max(1, GetDpiForWindow(hwnd) / 96f); } catch (EntryPointNotFoundException) { return 1; } }
        internal static bool IsCodex(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            try {
                using (Process p = Process.GetProcessById((int)pid)) {
                    if (p.ProcessName != "ChatGPT" && p.ProcessName != "Codex") return false;
                    string path = p.MainModule.FileName;
                    return path.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("OpenAI\\Codex", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            } catch { return false; }
        }
        internal static IntPtr FindCodexWindow()
        {
            IntPtr found=IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
                if(IsWindowVisible(hwnd) && GetWindow(hwnd,4)==IntPtr.Zero && GetWindowTextLength(hwnd)>0 && IsCodex(hwnd)) {
                    found=hwnd; return false;
                }
                return true;
            },IntPtr.Zero);
            return found;
        }
        internal static bool HasCodexGuiProcess()
        {
            int session;
            using(var own=Process.GetCurrentProcess()) session=own.SessionId;
            bool found=false;
            foreach(string name in new[]{"ChatGPT","Codex"})
                foreach(Process process in Process.GetProcessesByName(name))
                    using(process) { try {
                        if(process.SessionId==session && process.MainModule.FileName.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0)
                            found=true;
                    } catch { } }
            return found;
        }
        internal static bool UncoveredAtCenter(IntPtr hwnd)
        {
            Rect r;if(!IsWindowVisible(hwnd) || !GetWindowRect(hwnd,out r))return false;
            IntPtr top=WindowFromPoint(new Point((r.Left+r.Right)/2,(r.Top+r.Bottom)/2));
            return top==hwnd || GetAncestor(top,2)==hwnd;
        }
    }

    internal static class RuntimeLog
    {
        internal static void Write(string name, object details)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"run-history.jsonl"),
                new JavaScriptSerializer().Serialize(new {utc=DateTime.UtcNow.ToString("o"),pid=Process.GetCurrentProcess().Id,name=name,details=details})+Environment.NewLine); } catch { }
        }
    }

    internal sealed class BatteryForm : Form
    {
        private readonly System.Windows.Forms.Timer tracking = new System.Windows.Forms.Timer();
        private QuotaConnection connection;
        private QuotaState connectionState;
        private bool wasShowing;
        private readonly ToolTip tip = new ToolTip { AutoPopDelay=30000, InitialDelay=250, ReshowDelay=100 };
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly EventWaitHandle stop;
        private readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
        private UsageSnapshot snapshot;
        private bool fetching, failed, dragging;
        private string error = "";
        private IntPtr target;
        private float scale = 1;
        private float inset = 190, topOffset = 10;
        private Point dragStart;
        private float originalInset, originalTop;
        private string lastStatus = "";
        private HostLifetime lifetime;
        private bool wantUsage, pollingStarted, raisedLayer;
        private bool hostProcessAlive;
        private DateTime nextProcessCheck=DateTime.MinValue;
        private string loggedPhase="",exitReason="user_stopped";
        internal Func<QuotaConnection> TestConnectionFactory;
        internal bool TestMode;
        internal IntPtr TestTarget;
        internal bool TestHostProcess=false;
        internal bool LaunchRequested;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }

        internal BatteryForm(EventWaitHandle stopSignal, bool loadPosition=true)
        {
            stop=stopSignal;
            Text="Codex 用量电池"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual; DoubleBuffered=true; AutoScaleMode=AutoScaleMode.None;
            BackColor=Color.Magenta; TransparencyKey=Color.Magenta; Size=new Size(240,34);
            menu.Items.Add("立即刷新",null,delegate { RefreshUsage(); });
            menu.Items.Add("恢复默认位置",null,delegate { inset=190; topOffset=10; SavePosition(); Track(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出用量电池",null,delegate { Close(); });
            ContextMenuStrip=menu;
            MouseDown += delegate(object sender, MouseEventArgs e) { if(e.Button==MouseButtons.Left) {
                dragging=true; Capture=true; dragStart=Cursor.Position; originalInset=inset; originalTop=topOffset; } };
            MouseMove += delegate { if(dragging) {
                inset=Math.Max(145,originalInset-(Cursor.Position.X-dragStart.X)/scale);
                topOffset=Math.Max(0,Math.Min(60,originalTop+(Cursor.Position.Y-dragStart.Y)/scale)); Track(); } };
            MouseUp += delegate { if(dragging) { dragging=false; Capture=false; SavePosition(); } };
            if(loadPosition) LoadPosition();
            tracking.Interval=250; tracking.Tick += delegate { if(stop != null && stop.WaitOne(0)) { Close(); return; } Track(); };

        }
        internal void StartTracking(bool fetch)
        {
            IntPtr handle=Handle; wantUsage=fetch;
            lifetime=new HostLifetime(LaunchRequested,DateTime.UtcNow);
            tracking.Start(); Track();
        }
        private void LoadPosition()
        {
            try { var map=UsageSnapshot.Map(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(folder,"position.json"))));
                inset=Math.Max(145,Convert.ToSingle(UsageSnapshot.Get(map,"rightInset")));
                topOffset=Math.Max(0,Math.Min(60,Convert.ToSingle(UsageSnapshot.Get(map,"topOffset")))); } catch { }
        }
        private void SavePosition()
        { try { File.WriteAllText(Path.Combine(folder,"position.json"),new JavaScriptSerializer().Serialize(new { rightInset=inset, topOffset=topOffset })); } catch { } }
        internal void Track()
        {
            IntPtr front=Native.GetForegroundWindow();
            IntPtr candidate=TestMode ? TestTarget : front;
            if(!TestMode && candidate!=Handle && Native.IsCodex(candidate)) target=candidate;
            if(TestMode) target=candidate;
            if(!TestMode && (target==IntPtr.Zero || !Native.IsWindow(target) || !Native.IsWindowVisible(target)))
                target=Native.FindCodexWindow();
            bool hasHost=target!=IntPtr.Zero && Native.IsWindow(target) && Native.IsWindowVisible(target);
            DateTime now=DateTime.UtcNow;
            if(TestMode) hostProcessAlive=hasHost || TestHostProcess;
            else if(hasHost) hostProcessAlive=true;
            else if(now>=nextProcessCheck) { hostProcessAlive=Native.HasCodexGuiProcess(); nextProcessCheck=now.AddSeconds(1); }
            if(lifetime!=null && lifetime.ShouldExit(hasHost,hostProcessAlive,now)) {
                exitReason=lifetime.ExitReason; Close(); return;
            }
            if(!TestMode && lifetime!=null && loggedPhase!=lifetime.Phase) {
                loggedPhase=lifetime.Phase;
                RuntimeLog.Write("phase",new {phase=loggedPhase,hasHostProcess=hostProcessAlive,target=target.ToInt64()});
            }
            if(!hasHost && pollingStarted) PauseUsage();
            if(hasHost && wantUsage && !pollingStarted) { pollingStarted=true; RefreshUsage(); }
            SyncConnection();
            bool foreground=TestMode || front==target || front==Handle || menu.Visible || dragging;
            Native.Rect rect;
            bool canShow=target!=IntPtr.Zero && Native.IsWindow(target) && Native.IsWindowVisible(target) &&
                !Native.IsIconic(target) && foreground && Native.GetWindowRect(target,out rect);
            if(canShow) {
                Native.GetWindowRect(target,out rect); scale=Native.Scale(target);
                int width=(int)Math.Round(240*scale), height=(int)Math.Round(34*scale);
                int x=rect.Right-(int)Math.Round(inset*scale)-width, y=rect.Top+(int)Math.Round(topOffset*scale);
                x=Math.Max(rect.Left+8,x);
                // No native owner: host-window destruction must not destroy the monitor.
                var bounds=new Rectangle(x,y,width,height);
                if(Bounds!=bounds) { Bounds=bounds; Invalidate(); }
                if(!Visible) Show();
                // Hidden launchers can suppress the first native Show even when WinForms.Visible is true.
                if(!Native.IsWindowVisible(Handle)) Native.ShowWindow(Handle,4);
                if(!raisedLayer || !Native.UncoveredAtCenter(Handle)) {
                    Native.SetOverlayLayer(Handle,true); raisedLayer=true;
                }
            } else {
                if(Visible) Hide();
                if(raisedLayer) {Native.SetOverlayLayer(Handle,false);raisedLayer=false;}
            }
            if(canShow && !wasShowing && connection!=null && (snapshot==null || (DateTime.UtcNow-snapshot.FetchedUtc).TotalSeconds>3))connection.RequestRefresh();
            wasShowing=canShow;
            UpdateTip();
            WriteStatus();
        }
        private string Percent(int? n) { return n.HasValue ? n.Value+"%" : "未知"; }
        private void UpdateTip()
        {
            string text="Codex 剩余额度\n";
            if(snapshot==null) text += fetching ? "正在连接…" : "尚无可用数据";
            else {
                text+=UsageSnapshot.WindowName(snapshot.PrimaryMinutes,"主要额度")+"："+Percent(snapshot.PrimaryRemaining)+"，重置 "+UsageSnapshot.ResetText(snapshot.PrimaryReset)+"\n";
                text+=UsageSnapshot.WindowName(snapshot.SecondaryMinutes,"次要额度")+"："+Percent(snapshot.SecondaryRemaining)+"，重置 "+UsageSnapshot.ResetText(snapshot.SecondaryReset)+"\n";
                text+="最近收到数据 "+snapshot.FetchedUtc.ToLocalTime().ToString("HH:mm:ss")+"；持续连接，通知优先，30 秒补查";
            }
            if(failed) text+="\n连接失败："+error+"；将在下次自动重试";
            else if(snapshot!=null && snapshot.PrimaryIsStale) text+="\n数据已过期";
            if(snapshot!=null && snapshot.WeeklyFetchedUtc.HasValue)
                text+="\n周额度采样 "+snapshot.WeeklyFetchedUtc.Value.ToLocalTime().ToString("HH:mm:ss")+(snapshot.WeeklyIsStale ? "（已过期，等待校对）" : "");
            text+="\n"+NextUpdateText()+"；官方通知可能更早更新";
            text+="\n拖动调整位置；右键刷新或退出";
            if(tip.GetToolTip(this)!=text) { tip.SetToolTip(this,text); Invalidate(); }
        }
        internal void RefreshUsage()
        {
            if(TestMode && TestConnectionFactory==null)return;
            if(!TestMode && (target==IntPtr.Zero || !Native.IsWindowVisible(target)))return;
            try {if(connection==null)connection=TestConnectionFactory!=null ? TestConnectionFactory() : QuotaClient.Connect();connection.RequestRefresh();SyncConnection();}
            catch {failed=true;error="无法建立查询连接，请检查桌面配套程序";}
            UpdateTip();Invalidate();
        }
        private void PauseUsage()
        {
            if(connection!=null)connection.Dispose();
            if(!TestMode)QuotaClient.Shutdown();
            connection=null;connectionState=null;snapshot=null;pollingStarted=false;
            fetching=false;failed=false;error="";
        }
        private void SyncConnection()
        {
            if(connection==null)return;
            QuotaState current=connection.ReadState();
            bool changed=connectionState==null || snapshot!=current.Snapshot || connectionState.Busy!=current.Busy ||
                connectionState.NextUpdateUtc!=current.NextUpdateUtc || connectionState.Error!=current.Error;
            connectionState=current;snapshot=current.Snapshot;fetching=current.Busy;failed=current.Error!=null;error=current.Error ?? "";
            if(changed)Invalidate();
        }
        private string ResetClockText()
        { return snapshot==null ? "5小时重置 未知" : snapshot.FiveHourResetLabel; }
        private string NextUpdateText()
        {
            if(fetching)return "正在更新…";
            if(connectionState==null || !connectionState.NextUpdateUtc.HasValue)return "等待连接";
            return (failed ? "重试 " : "下次补查 ")+connectionState.NextUpdateUtc.Value.ToLocalTime().ToString("HH:mm:ss");
        }
        private void WriteStatus()
        {
            if(TestMode) return;
            var data=new { running=true,pid=Process.GetCurrentProcess().Id,phase=lifetime==null ? null : lifetime.Phase,
                hasHostProcess=hostProcessAlive,visible=Visible,nativeVisible=Native.IsWindowVisible(Handle),
                uncoveredAtCenter=Visible && Native.UncoveredAtCenter(Handle),
                targetHandle=target.ToInt64(), left=Left, top=Top, width=Width, height=Height,
                primaryRemaining=snapshot==null ? null : snapshot.PrimaryRemaining,
                secondaryRemaining=snapshot==null ? null : snapshot.SecondaryRemaining,
                fetchedUtc=snapshot==null ? null : (string)snapshot.FetchedUtc.ToString("o"),
                failed=failed, stale=snapshot!=null && snapshot.PrimaryIsStale, fetching=fetching,
                weeklyRemaining=snapshot==null ? null : snapshot.WeeklyRemaining,
                fiveHourReset=snapshot==null ? null : snapshot.FiveHourReset,clockText=ResetClockText(),
                nextUpdateUtc=connectionState==null || !connectionState.NextUpdateUtc.HasValue ? null : (string)connectionState.NextUpdateUtc.Value.ToString("o"),
                queryProcessId=connectionState==null ? null : connectionState.ProcessId,
                queryProcessStarts=connectionState==null ? 0 : connectionState.ProcessStarts,
                queryReads=connectionState==null ? 0 : connectionState.Reads,
                notifications=connectionState==null ? 0 : connectionState.Notifications,
                connectionSource=connectionState==null ? null : connectionState.Source,connectionMode="persistent",weeklyStale=snapshot!=null && snapshot.WeeklyIsStale };
            string json=new JavaScriptSerializer().Serialize(data);
            if(json==lastStatus) return;
            try { File.WriteAllText(Path.Combine(folder,"status.json"),json); lastStatus=json; } catch { }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            bool stale=failed || (snapshot!=null && snapshot.PrimaryIsStale);
            int? value=snapshot==null ? null : snapshot.PrimaryRemaining;
            string label=stale ? "?" : value.HasValue ? value.Value+"%" : fetching ? "…" : "--";
            DrawPanel(e.Graphics,new Rectangle(0,0,Width,Height),value,label,snapshot==null ? null : snapshot.WeeklyRemaining,ResetClockText(),stale,failed || (snapshot!=null && snapshot.WeeklyIsStale));
        }
        internal static void DrawPanel(Graphics g,Rectangle area,int? remaining,string label,int? weekly,string next,bool stale,bool? weeklyStale=null)
        {
            GraphicsState state=g.Save();g.TranslateTransform(area.Left,area.Top);g.ScaleTransform(area.Width/240f,area.Height/34f);
            DrawBattery(g,new Rectangle(0,3,74,28),remaining,label,stale);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var background=RoundRect(new RectangleF(79,1,160,32),6))
            using(var brush=new SolidBrush(Color.FromArgb(24,29,35)))g.FillPath(brush,background);
            Color accent=stale ? Color.FromArgb(155,166,180) : Color.FromArgb(103,205,231);
            using(var pen=new Pen(accent,1.4f)) {
                g.DrawEllipse(pen,85,9,15,15);g.DrawLine(pen,92.5f,12,92.5f,17);g.DrawLine(pen,92.5f,17,96,19);
            }
            using(var font=new Font("Microsoft YaHei UI",9,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var text=new SolidBrush(Color.FromArgb(239,243,248))) {
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(next,font,text,new RectangleF(106,2,130,15));
                string week=(weeklyStale ?? stale) ? "?" : weekly.HasValue ? weekly.Value+"%" : "未知";
                g.DrawString("本周剩余 "+week,font,text,new RectangleF(106,17,130,15));
            }
            g.Restore(state);
        }
        internal static void DrawBattery(Graphics g, Rectangle area, int? remaining, string label, bool stale)
        {
            GraphicsState state=g.Save();
            g.TranslateTransform(area.Left,area.Top); g.ScaleTransform(area.Width/74f,area.Height/28f);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            Color accent=stale || !remaining.HasValue ? Color.FromArgb(155,166,180) :
                remaining.Value<=10 ? Color.FromArgb(245,99,110) : remaining.Value<=20 ? Color.FromArgb(244,184,71) : Color.FromArgb(69,210,164);
            using(var path=RoundRect(new RectangleF(2,2,64,24),5))
            using(var bg=new SolidBrush(Color.FromArgb(24,29,35)))
            using(var pen=new Pen(accent,1.5f)) { g.FillPath(bg,path); g.DrawPath(pen,path); }
            using(var brush=new SolidBrush(accent)) { g.FillRectangle(brush,68,9,3,10); }
            if(remaining.HasValue && !stale && remaining.Value>0) {
                using(var clip=RoundRect(new RectangleF(5,5,58,18),2)) {
                    g.SetClip(clip); using(var fill=new SolidBrush(Color.FromArgb(95,accent))) g.FillRectangle(fill,5,5,58*remaining.Value/100f,18);
                    g.ResetClip();
                }
            }
            using(var font=new Font("Segoe UI",11,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(Color.FromArgb(245,248,250)))
            using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center }) {
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(label,font,brush,new RectangleF(3,2,62,24),format);
            }
            g.Restore(state);
        }
        private static GraphicsPath RoundRect(RectangleF r,float radius)
        { float d=radius*2; var p=new GraphicsPath(); p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p; }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tracking.Stop();
            if(connection!=null)connection.Dispose();
            if(!TestMode) QuotaClient.Shutdown();
            if(!TestMode) RuntimeLog.Write("exit",new {reason=exitReason});
            try { if(!TestMode) File.WriteAllText(Path.Combine(folder,"status.json"),new JavaScriptSerializer().Serialize(new {
                visible=false,running=false,exitedUtc=DateTime.UtcNow.ToString("o"),reason=exitReason })); } catch { }
            tracking.Dispose(); tip.Dispose(); menu.Dispose(); base.OnFormClosed(e);
        }
    }

    internal static class Program
    {
        internal const string StopName="Local\\CodexUsageBattery.Stop.v1";
        [STAThread] private static void Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch(EntryPointNotFoundException) { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string folder=AppDomain.CurrentDomain.BaseDirectory;
            if(args.Contains("--stop")) { try { using(var signal=EventWaitHandle.OpenExisting(StopName)) signal.Set(); } catch(WaitHandleCannotBeOpenedException) { } return; }
            if(args.Contains("--preview")) { Preview(Path.Combine(folder,"preview.png")); return; }
            if(args.Contains("--probe")) { Probe(Path.Combine(folder,"probe.json")); return; }
            if(args.Contains("--ui-test")) { UiTest(Path.Combine(folder,"ui-test.json")); return; }
            // Attachment never launches the Codex desktop application.
            bool awaitHost=args.Contains("--attach");
            bool created;
            using(var mutex=new Mutex(true,"Local\\CodexUsageBattery.Instance.v1",out created)) {
                if(!created) return;
                RuntimeLog.Write("start",new {mode=awaitHost ? "attach" : "direct"});
                using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,StopName))
                using(var form=new BatteryForm(stop) { LaunchRequested=awaitHost }) {
                    var context=new ApplicationContext();
                    form.FormClosed += delegate { context.ExitThread(); };
                    form.StartTracking(true); if(!form.IsDisposed) Application.Run(context);
                }
            }
        }
        private static void Probe(string path)
        {
            object result;
            try { var s=QuotaClient.Fetch(); result=new { ok=true,primaryRemaining=s.PrimaryRemaining,secondaryRemaining=s.SecondaryRemaining,
                primaryMinutes=s.PrimaryMinutes,secondaryMinutes=s.SecondaryMinutes,fetchedUtc=s.FetchedUtc.ToString("o") }; }
            catch(Exception e) { result=new { ok=false, errorType=e.GetType().Name }; Environment.ExitCode=1; }
            QuotaClient.Shutdown();
            File.WriteAllText(path,new JavaScriptSerializer().Serialize(result));
        }
        private static void Preview(string path)
        {
            using(var bmp=new Bitmap(1000,220))using(var g=Graphics.FromImage(bmp)) {
                g.Clear(Color.FromArgb(18,21,26));
                DrawPreview(g,new Rectangle(20,20,460,65),64,"64%",89,"5小时重置 12:30",false);
                DrawPreview(g,new Rectangle(515,20,460,65),8,"8%",12,"5小时重置 12:30",false);
                DrawPreview(g,new Rectangle(20,120,460,65),null,"--",null,"5小时重置 未知",false);
                DrawPreview(g,new Rectangle(515,120,460,65),null,"?",null,"5小时重置 未知",true);
                bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        private static void DrawPreview(Graphics g,Rectangle area,int? main,string label,int? week,string next,bool stale)
        {BatteryForm.DrawPanel(g,area,main,label,week,next,stale);}
        private static void UiTest(string path)
        {
            var checks=new List<string>(); string failure=null;
            using(var host=new Form { Text="Codex Battery 自检窗口",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(120,120,900,400) })
            using(var battery=new BatteryForm(null,false) { TestMode=true }) {
                var timer=new System.Windows.Forms.Timer { Interval=350 }; int step=0,firstX=0,firstY=0;
                host.Shown += delegate { Native.ShowWindow(host.Handle,4); battery.TestTarget=host.Handle; battery.StartTracking(false); timer.Start(); };
                timer.Tick += delegate {
                    try {
                        if(step==0) { battery.Track(); if(!battery.Visible) throw new Exception("initial visibility; hostVisible="+host.Visible+" nativeVisible="+Native.IsWindowVisible(host.Handle)+" minimized="+Native.IsIconic(host.Handle)+" testTarget="+battery.TestTarget+" batteryNativeVisible="+Native.IsWindowVisible(battery.Handle)); if(!Native.UncoveredAtCenter(battery.Handle))throw new Exception("battery is covered despite native visibility");checks.Add("visible and uncovered on host"); firstX=battery.Left; firstY=battery.Top; host.Left+=100; host.Top+=40; }
                        else if(step==1) { battery.Track(); if(battery.Left-firstX!=100 || battery.Top-firstY!=40) throw new Exception("movement follow"); checks.Add("follows host move"); host.Width+=150; }
                        else if(step==2) { battery.Track(); if(battery.Left-firstX!=250) throw new Exception("resize follow; delta="+(battery.Left-firstX)+" hostWidth="+host.Width); checks.Add("follows resize"); host.WindowState=FormWindowState.Minimized; }
                        else if(step==3) { battery.Track(); if(battery.Visible) throw new Exception("minimize hide"); checks.Add("hides on minimize"); host.WindowState=FormWindowState.Normal; }
                        else if(step==4) { battery.Track(); if(!battery.Visible) throw new Exception("restore visibility"); checks.Add("returns on restore"); battery.TestTarget=IntPtr.Zero; battery.Track(); if(battery.Visible) throw new Exception("closed target hide"); checks.Add("hides when target unavailable"); host.Close(); }
                        step++;
                    } catch(Exception e) { failure=e.Message; host.Close(); }
                };
                Application.Run(host); timer.Dispose(); battery.Close();
            }
            File.WriteAllText(path,new JavaScriptSerializer().Serialize(new { ok=failure==null, checks=checks, failure=failure }));
            if(failure!=null) Environment.ExitCode=1;
        }
    }
}
