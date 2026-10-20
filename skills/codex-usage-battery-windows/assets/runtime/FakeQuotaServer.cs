using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;

internal static class FakeQuotaServer
{
    private static readonly object output=new object();
    private static void Send(object value) { lock(output) Console.WriteLine(new JavaScriptSerializer().Serialize(value)); }
    private static object Limits(int used) {return new {rateLimits=new {limitId="codex",primary=new {usedPercent=used,windowDurationMins=300,resetsAt=1800000000},secondary=new {usedPercent=20,windowDurationMins=10080,resetsAt=1800400000}}};}
    public static void Main(string[] args)
    {
        string mode=args.Length>1 ? args[1] : "normal";
        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fake-starts.txt"),Process.GetCurrentProcess().Id+Environment.NewLine);
        int reads=0;
        string line;
        while((line=Console.ReadLine())!=null) {
            var message=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);
            string method=message.ContainsKey("method") ? Convert.ToString(message["method"]) : "";
            object id=message.ContainsKey("id") ? message["id"] : null;
            if(method=="initialize") { if(mode!="hang") Send(new {id=id,result=new {userAgent="quota-test"}}); }
            else if(method=="account/rateLimits/read") {
                reads++;
                if(mode=="auth-inflight" && reads==1) {
                    Send(new {method="account/updated",@params=new {authMode="chatgpt"}});
                    Send(new {id=id,result=Limits(25)});continue;
                }
                if(mode=="error-notify") {
                    Send(new {id=id,error=new {code=-32000,message="test failure"}});
                    Send(new {method="account/rateLimits/updated",@params=new {rateLimits=new {limitId="codex",primary=new {usedPercent=31}}}});continue;
                }
                if(mode=="auth" && reads>1) {Send(new {id=id,error=new {code=401,message="test unauthorized"}});continue;}
                if(mode=="error") {Send(new {id=id,error=new {code=-32000,message="test failure"}});continue;}
                if(mode=="read-hang") continue;
                Send(new {id=id,result=Limits(mode=="auth-inflight" ? 55 : reads==1 ? 25 : 35)});
                if(mode=="disconnect") return;
                if(mode=="notify" && reads==1) {
                    Thread.Sleep(100);
                    Send(new {method="account/rateLimits/updated",@params=new {rateLimits=new {limitId="codex",primary=new {usedPercent=31,windowDurationMins=300,resetsAt=1800000000}}}});
                }
                if(mode=="auth" && reads==1) {Thread.Sleep(100);Send(new {method="account/updated",@params=new {authMode=(string)null}});}
            }
        }
    }
}
