using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CodexBattery
{
    public sealed class UsageSnapshot
    {
        public int? PrimaryRemaining { get; private set; }
        public int? SecondaryRemaining { get; private set; }
        public int? PrimaryMinutes { get; private set; }
        public int? SecondaryMinutes { get; private set; }
        public long? PrimaryReset { get; private set; }
        public long? SecondaryReset { get; private set; }
        public DateTime FetchedUtc { get; private set; }
        public DateTime? PrimaryFetchedUtc { get; private set; }
        public DateTime? SecondaryFetchedUtc { get; private set; }
        public bool PrimaryIsStale {get {return PrimaryFetchedUtc.HasValue && IsStale(PrimaryFetchedUtc.Value);}}
        public DateTime? WeeklyFetchedUtc {get {return PrimaryMinutes==10080 ? PrimaryFetchedUtc : SecondaryMinutes==10080 ? SecondaryFetchedUtc : null;}}
        public bool WeeklyIsStale {get {return WeeklyFetchedUtc.HasValue && IsStale(WeeklyFetchedUtc.Value);}}

        public static IDictionary<string, object> Map(object value)
        { return value as IDictionary<string, object>; }

        public static object Get(IDictionary<string, object> map, string key)
        { object value; return map != null && map.TryGetValue(key, out value) ? value : null; }

        private static double? Number(IDictionary<string, object> map, string key)
        {
            object value = Get(map, key);
            double number;
            if (value == null || !Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out number) || Double.IsNaN(number) || Double.IsInfinity(number)) return null;
            return number;
        }

        private static int? Remaining(IDictionary<string, object> window)
        {
            double? used = Number(window, "usedPercent");
            return used.HasValue ? (int?)Math.Floor(Math.Max(0, Math.Min(100, 100 - used.Value))) : null;
        }

        public static UsageSnapshot Parse(string json)
        {
            IDictionary<string, object> root = Map(new JavaScriptSerializer().DeserializeObject(json));
            if (root == null || Get(root, "error") != null) throw new InvalidOperationException("用量服务返回错误");
            IDictionary<string, object> buckets = Map(Get(root, "rateLimitsByLimitId"));
            IDictionary<string, object> limits = buckets == null ? Map(Get(root, "rateLimits")) : Map(Get(buckets, "codex"));
            object bucketId=Get(limits,"limitId");
            if(bucketId!=null && Convert.ToString(bucketId)!="codex")limits=null;
            IDictionary<string, object> first = Map(Get(limits, "primary"));
            IDictionary<string, object> second = Map(Get(limits, "secondary"));
            double? pmins = Number(first, "windowDurationMins"), smins = Number(second, "windowDurationMins");
            double? preset = Number(first, "resetsAt"), sreset = Number(second, "resetsAt");
            return new UsageSnapshot {
                PrimaryRemaining = Remaining(first), SecondaryRemaining = Remaining(second),
                PrimaryMinutes = pmins.HasValue ? (int?)pmins.Value : null,
                SecondaryMinutes = smins.HasValue ? (int?)smins.Value : null,
                PrimaryReset = preset.HasValue ? (long?)preset.Value : null,
                SecondaryReset = sreset.HasValue ? (long?)sreset.Value : null,
                FetchedUtc = DateTime.UtcNow,
                PrimaryFetchedUtc=first==null ? null : (DateTime?)DateTime.UtcNow,
                SecondaryFetchedUtc=second==null ? null : (DateTime?)DateTime.UtcNow
            };
        }

        public long? FiveHourReset {
            get {return PrimaryMinutes==300 ? PrimaryReset : SecondaryMinutes==300 ? SecondaryReset : null;}
        }
        public string FiveHourResetLabel {
            get {
                long? reset=FiveHourReset;
                if(reset.HasValue) {
                    try {return "5小时重置 "+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(reset.Value).ToLocalTime().ToString("HH:mm");}
                    catch(ArgumentOutOfRangeException) {}
                }
                return "5小时重置 未知";
            }
        }
        public int? WeeklyRemaining {
            get {return PrimaryMinutes==10080 ? PrimaryRemaining : SecondaryMinutes==10080 ? SecondaryRemaining : null;}
        }
        public static UsageSnapshot MergeNotification(string json,UsageSnapshot previous)
        {
            var root=Map(new JavaScriptSerializer().DeserializeObject(json));
            var data=Map(Get(root,"params")) ?? root;
            var buckets=Map(Get(data,"rateLimitsByLimitId"));
            var limits=buckets!=null ? Map(Get(buckets,"codex")) : Map(Get(data,"rateLimits"));
            if(limits==null || (buckets==null && Convert.ToString(Get(limits,"limitId"))!="codex"))return previous;
            var result=previous==null ? new UsageSnapshot() : new UsageSnapshot {
                PrimaryRemaining=previous.PrimaryRemaining,SecondaryRemaining=previous.SecondaryRemaining,
                PrimaryMinutes=previous.PrimaryMinutes,SecondaryMinutes=previous.SecondaryMinutes,
                PrimaryReset=previous.PrimaryReset,SecondaryReset=previous.SecondaryReset,
                PrimaryFetchedUtc=previous.PrimaryFetchedUtc,SecondaryFetchedUtc=previous.SecondaryFetchedUtc};
            bool changed=false;
            foreach(string key in new[]{"primary","secondary"}) {
                if(!limits.ContainsKey(key))continue;
                bool primary=key=="primary";var window=Map(Get(limits,key));
                if(window==null) {
                    if(primary){result.PrimaryRemaining=null;result.PrimaryMinutes=null;result.PrimaryReset=null;result.PrimaryFetchedUtc=null;}
                    else{result.SecondaryRemaining=null;result.SecondaryMinutes=null;result.SecondaryReset=null;result.SecondaryFetchedUtc=null;}
                    changed=true;continue;
                }
                if(window.ContainsKey("usedPercent")) {
                    if(primary){result.PrimaryRemaining=Remaining(window);result.PrimaryFetchedUtc=DateTime.UtcNow;}
                    else{result.SecondaryRemaining=Remaining(window);result.SecondaryFetchedUtc=DateTime.UtcNow;}
                    changed=true;
                }
                if(window.ContainsKey("windowDurationMins")) {
                    double? n=Number(window,"windowDurationMins");
                    if(primary)result.PrimaryMinutes=n.HasValue ? (int?)n.Value : null;
                    else result.SecondaryMinutes=n.HasValue ? (int?)n.Value : null;
                    changed=true;
                }
                if(window.ContainsKey("resetsAt")) {
                    double? n=Number(window,"resetsAt");
                    if(primary)result.PrimaryReset=n.HasValue ? (long?)n.Value : null;
                    else result.SecondaryReset=n.HasValue ? (long?)n.Value : null;
                    changed=true;
                }
            }
            if(!changed)return previous;
            result.FetchedUtc=DateTime.UtcNow;return result;
        }

        public static bool IsStale(DateTime fetchedUtc)
        { return DateTime.UtcNow - fetchedUtc > TimeSpan.FromMinutes(2); }

        public static string WindowName(int? minutes, string fallback)
        {
            if (!minutes.HasValue) return fallback;
            if (minutes.Value == 10080) return "每周额度";
            if (minutes.Value % 60 == 0) return (minutes.Value / 60) + " 小时额度";
            return minutes.Value + " 分钟额度";
        }

        public static string ResetText(long? unix)
        {
            if (!unix.HasValue) return "未知";
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix.Value).ToLocalTime().ToString("MM-dd HH:mm"); }
            catch (ArgumentOutOfRangeException) { return "未知"; }
        }
    }
}
