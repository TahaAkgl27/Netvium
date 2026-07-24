using System;
using System.Collections.Generic;
using Netvium.Helpers;

namespace Netvium.Models
{
    /// <summary>
    /// Disk-persisted dashboard state for stale-while-revalidate loading.
    /// </summary>
    public sealed class PersistentServerSnapshot
    {
        public string Version { get; set; } = "1";
        public string ServerKeyHash { get; set; } = "";
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public SnapshotSectionTimestamps SectionTimestamps { get; set; } = new();
        public WmiSnapshot Metrics { get; set; } = new();
        public List<ServiceRow> Services { get; set; } = new();
        public List<TimelineEvent> Timeline { get; set; } = new();
        public List<SmartInsight> AiInsights { get; set; } = new();
        public List<SmartInsight> RuleInsights { get; set; } = new();
        public List<DetectedComponent> SystemStatusComponents { get; set; } = new();
        public string? DetectedRole { get; set; }
        public int IntelligenceConfidence { get; set; }
        /// <summary>FullWmi = dashboard/WMI data; BasicProbe = ping/RDP/health only.</summary>
        public string SnapshotKind { get; set; } = SnapshotKinds.BasicProbe;
        public SnapshotTrendData? Trends { get; set; }
    }
}
