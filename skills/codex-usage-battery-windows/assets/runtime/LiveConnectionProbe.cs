using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using CodexBattery;

internal static class LiveConnectionProbe
{
    public static void Main()
    {
        object result;
        int? pid=null;
        try {
            var connection=QuotaClient.Connect();
            DateTime started=DateTime.UtcNow,end=started.AddSeconds(95);
            while(DateTime.UtcNow<end) {
                var current=connection.ReadState();
                if(current.Reads>=3 && !current.Busy)break;
                Thread.Sleep(100);
            }
            var s=connection.ReadState();pid=s.ProcessId;
            bool ok=s.Reads>=3 && s.ProcessStarts==1 && s.Snapshot!=null;
            result=new {ok=ok,elapsedSeconds=Math.Round((DateTime.UtcNow-started).TotalSeconds,1),reads=s.Reads,processStarts=s.ProcessStarts,queryPid=s.ProcessId,
                notifications=s.Notifications,source=s.Source,nextUpdateUtc=s.NextUpdateUtc,
                primaryRemaining=s.Snapshot==null ? null : s.Snapshot.PrimaryRemaining,
                weeklyRemaining=s.Snapshot==null ? null : s.Snapshot.WeeklyRemaining,error=s.Error};
            if(!ok)Environment.ExitCode=1;
        }catch(Exception e){result=new {ok=false,errorType=e.GetType().Name};Environment.ExitCode=1;}
        finally{QuotaClient.Shutdown();}
        if(pid.HasValue) {try{using(var p=Process.GetProcessById(pid.Value)){if(!p.HasExited)throw new Exception("query child survived shutdown");}}catch(ArgumentException){}}
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-connection-probe.json"),new JavaScriptSerializer().Serialize(result));
    }
}
