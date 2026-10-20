using System;
using System.IO;
using CodexBattery;

internal static class ReuseRegression
{
    public static void Main()
    {
        try {
            UsageSnapshot first=QuotaClient.Fetch();
            UsageSnapshot second=QuotaClient.Fetch();
            string exe=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codex-executable.txt")).Trim();
            int starts=File.ReadAllLines(Path.Combine(Path.GetDirectoryName(exe),"fake-starts.txt")).Length;
            if(starts!=1) throw new Exception("Expected one query process for two reads; observed "+starts);
            if(second.PrimaryRemaining!=65)throw new Exception("Second read must reuse server state and return new quota");
            Console.WriteLine("PASS two reads reuse one process and receive independent fresh replies");
        } catch(Exception e) {Console.WriteLine("FAIL: "+e.Message);Environment.ExitCode=1;}
        finally {QuotaClient.Shutdown();}
    }
}
