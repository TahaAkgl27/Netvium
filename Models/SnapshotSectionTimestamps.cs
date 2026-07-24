using System;

namespace Netvium.Models
{
    /// <summary>
    /// Per-section UTC timestamps for stale-while-revalidate TTL checks.
    /// </summary>
    public sealed class SnapshotSectionTimestamps
    {
        public DateTime? OsInfoUtc { get; set; }
        public DateTime? HardwareInfoUtc { get; set; }
        public DateTime? NetworkInfoUtc { get; set; }
        public DateTime? ServicesUtc { get; set; }
        public DateTime? MetricsUtc { get; set; }
        public DateTime? LatencyUtc { get; set; }
        public DateTime? TimelineUtc { get; set; }
        public DateTime? AiAnalysisUtc { get; set; }
        public DateTime? GeneralInfoUtc { get; set; }
    }

    public static class SnapshotTtl
    {
        public static readonly TimeSpan OsInfo = TimeSpan.FromHours(24);
        public static readonly TimeSpan HardwareInfo = TimeSpan.FromHours(24);
        public static readonly TimeSpan NetworkInfo = TimeSpan.FromHours(6);
        public static readonly TimeSpan Services = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan Metrics = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan Latency = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan Timeline = TimeSpan.FromMinutes(12);
        public static readonly TimeSpan AiAnalysis = TimeSpan.FromHours(6);

        public static bool IsExpired(DateTime? sectionUtc, TimeSpan ttl) =>
            !sectionUtc.HasValue || DateTime.UtcNow - sectionUtc.Value.ToUniversalTime() > ttl;
    }
}
