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
                FetchedUtc = DateTime.UtcNow
            };
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
