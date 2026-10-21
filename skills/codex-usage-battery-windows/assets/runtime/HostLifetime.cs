using System;

namespace CodexBattery
{
    public sealed class HostLifetime
    {
        private readonly bool launchRequested;
        private readonly DateTime startedUtc;
        private bool seenHost;
        private bool seenProcess;
        private DateTime? missingSinceUtc;
        public string Phase { get; private set; }
        public string ExitReason { get; private set; }

        public HostLifetime(bool launchRequested, DateTime startedUtc)
        { this.launchRequested=launchRequested; this.startedUtc=startedUtc; }

        public bool ShouldExit(bool hasHostWindow, DateTime nowUtc)
        { return ShouldExit(hasHostWindow,false,nowUtc); }

        public bool ShouldExit(bool hasHostWindow, bool hasHostProcess, DateTime nowUtc)
        {
            if(hasHostWindow) { seenHost=true; seenProcess=true; missingSinceUtc=null; Phase="attached"; return false; }
            if(hasHostProcess) {
                seenProcess=true; missingSinceUtc=null; Phase=seenHost ? "paused_no_window" : "waiting_for_window"; return false;
            }
            if(!seenHost && !seenProcess) {
                Phase="waiting_for_process";
                bool absent=!launchRequested || nowUtc-startedUtc>=TimeSpan.FromSeconds(30);
                if(absent) ExitReason="host_process_never_started";
                return absent;
            }
            Phase=seenHost ? "window_closing" : "process_exiting";
            if(!missingSinceUtc.HasValue) missingSinceUtc=nowUtc;
            bool ended=nowUtc-missingSinceUtc.Value>=TimeSpan.FromSeconds(1);
            if(ended) ExitReason=seenHost ? "host_process_exited" : "host_process_exited_before_window";
            return ended;
        }
    }
}
