using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    /// <summary>
    /// Maps Start Analysis / daily probe results into dashboard persistent snapshots.
    /// </summary>
    public static class AnalysisSnapshotMapper
    {
        public static PersistentServerSnapshot FromAnalysis(
            ServerInfo server,
            HealthBadge badge,
            WmiSnapshot? snap,
            long? pingMs,
            bool rdpOpen,
            string? healthReason)
        {
            var now = DateTime.UtcNow;
            var metrics = PrepareMetrics(server, snap, pingMs);
            var timestamps = BuildTimestamps(metrics, pingMs, now);

            var timeline = metrics.Timeline?.Count > 0
                ? metrics.Timeline.ToList()
                : BuildMinimalTimeline(badge, healthReason, pingMs, rdpOpen);

            if (timeline.Count > 0)
                timestamps.TimelineUtc = now;

            var ruleInsights = BuildRuleInsights(badge, healthReason, rdpOpen, pingMs);
            if (ruleInsights.Count > 0)
                timestamps.GeneralInfoUtc = now;

            var isFull = SnapshotMergeHelper.IsFullWmiMetrics(metrics);

            return new PersistentServerSnapshot
            {
                SnapshotKind = isFull ? SnapshotKinds.FullWmi : SnapshotKinds.BasicProbe,
                Metrics = metrics,
                Services = metrics.Services?.Count > 0 ? metrics.Services.ToList() : new List<ServiceRow>(),
                Timeline = timeline,
                RuleInsights = ruleInsights,
                SystemStatusComponents = isFull
                    ? new List<DetectedComponent>()
                    : BuildHealthComponents(badge, rdpOpen, pingMs, metrics),
                SectionTimestamps = timestamps,
                LastUpdatedUtc = now,
                DetectedRole = badge switch
                {
                    HealthBadge.Healthy => "Operational",
                    HealthBadge.Warning => "Needs Attention",
                    HealthBadge.Critical => "At Risk",
                    _ => "Unreachable"
                },
                IntelligenceConfidence = badge switch
                {
                    HealthBadge.Healthy => 85,
                    HealthBadge.Warning => 55,
                    HealthBadge.Critical => 30,
                    _ => 10
                }
            };
        }

        public static IReadOnlyList<string> DescribeSections(PersistentServerSnapshot snapshot)
        {
            var sections = new List<string> { "General", "Health" };
            var m = snapshot.Metrics;
            if (m.CpuPct.HasValue || m.RamUsedPct.HasValue || m.DiskUsedPct.HasValue)
                sections.Add("Metrics");
            if (snapshot.SectionTimestamps.LatencyUtc.HasValue || m.PingMs >= 0)
                sections.Add("Latency");
            if (snapshot.SystemStatusComponents.Any(c =>
                    c.Name.Equals("RDP", StringComparison.OrdinalIgnoreCase)))
                sections.Add("Rdp");
            if (!string.IsNullOrWhiteSpace(m.OSName))
                sections.Add("Os");
            if (snapshot.Services.Count > 0)
                sections.Add("Services");
            if (snapshot.Timeline.Count > 0)
                sections.Add("Timeline");
            if (snapshot.RuleInsights.Count > 0)
                sections.Add("Rules");
            if (snapshot.AiInsights.Count > 0)
                sections.Add("AI");
            return sections.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IReadOnlyList<string> DescribePreservedSections(
            PersistentServerSnapshot existing,
            PersistentServerSnapshot incoming)
        {
            var preserved = new List<string>();
            if (incoming.AiInsights.Count == 0 && existing.AiInsights.Count > 0)
                preserved.Add("AI");
            if ((incoming.Timeline.Count == 0 || !HasNonSystemTimeline(incoming.Timeline))
                && existing.Timeline.Count > 0)
                preserved.Add("Timeline");
            if (string.IsNullOrWhiteSpace(incoming.Metrics?.OSName) && !string.IsNullOrWhiteSpace(existing.Metrics?.OSName))
                preserved.Add("Os");
            if (incoming.Services.Count == 0 && existing.Services.Count > 0)
                preserved.Add("Services");
            if (!(incoming.Metrics?.CpuPct).HasValue && (existing.Metrics?.CpuPct).HasValue)
                preserved.Add("Metrics");
            if (incoming.RuleInsights.Count == 0 && existing.RuleInsights.Count > 0)
                preserved.Add("Rules");
            if (string.IsNullOrWhiteSpace(incoming.DetectedRole) && !string.IsNullOrWhiteSpace(existing.DetectedRole))
                preserved.Add("General");
            return preserved;
        }

        private static WmiSnapshot PrepareMetrics(ServerInfo server, WmiSnapshot? snap, long? pingMs)
        {
            WmiSnapshot metrics;
            if (snap != null && snap.Connected)
            {
                metrics = snap;
            }
            else
            {
                metrics = snap != null ? CloneMetrics(snap) : new WmiSnapshot();
                metrics.Connected = snap?.Connected == true;
            }

            metrics.Ip = ServerSnapshotKey.NormalizeHost(server.Ip);
            metrics.Username = ServerSnapshotKey.NormalizeUsername(server.Username);
            if (pingMs.HasValue)
                metrics.PingMs = (int)Math.Min(pingMs.Value, int.MaxValue);
            if (metrics.CollectedAt == default)
                metrics.CollectedAt = DateTime.Now;

            return metrics;
        }

        private static SnapshotSectionTimestamps BuildTimestamps(WmiSnapshot metrics, long? pingMs, DateTime now)
        {
            var ts = new SnapshotSectionTimestamps { GeneralInfoUtc = now };
            if (metrics.CpuPct.HasValue || metrics.RamUsedPct.HasValue || metrics.DiskUsedPct.HasValue)
                ts.MetricsUtc = now;
            if (pingMs.HasValue)
                ts.LatencyUtc = now;
            if (!string.IsNullOrWhiteSpace(metrics.OSName))
                ts.OsInfoUtc = now;
            if (metrics.CPUCoreCount.HasValue || metrics.TotalRamGb.HasValue)
                ts.HardwareInfoUtc = now;
            if (metrics.IPAddresses?.Count > 0 || !string.IsNullOrWhiteSpace(metrics.Ip))
                ts.NetworkInfoUtc = now;
            if (metrics.Services?.Count > 0)
                ts.ServicesUtc = now;
            if (metrics.Timeline?.Count > 0)
                ts.TimelineUtc = now;
            return ts;
        }

        private static List<SmartInsight> BuildRuleInsights(
            HealthBadge badge,
            string? healthReason,
            bool rdpOpen,
            long? pingMs)
        {
            var insights = new List<SmartInsight>();
            var severity = badge switch
            {
                HealthBadge.Critical => InsightSeverity.Critical,
                HealthBadge.Warning => InsightSeverity.Warning,
                HealthBadge.Offline => InsightSeverity.Critical,
                _ => InsightSeverity.Info
            };

            if (!string.IsNullOrWhiteSpace(healthReason))
                insights.Add(new SmartInsight(healthReason, severity, "📊"));

            if (!rdpOpen)
                insights.Add(new SmartInsight("RDP portu kapalı veya erişilemiyor.", InsightSeverity.Warning, "🔌"));

            if (pingMs is >= 150)
                insights.Add(new SmartInsight($"Yüksek gecikme: {pingMs.Value}ms", InsightSeverity.Warning, "📡"));

            return insights;
        }

        private static List<TimelineEvent> BuildMinimalTimeline(
            HealthBadge badge,
            string? healthReason,
            long? pingMs,
            bool rdpOpen)
        {
            var level = badge switch
            {
                HealthBadge.Critical or HealthBadge.Offline => "Warning",
                HealthBadge.Warning => "Warning",
                _ => "Info"
            };
            var message = !string.IsNullOrWhiteSpace(healthReason)
                ? healthReason
                : badge switch
                {
                    HealthBadge.Healthy => $"Sunucu erişilebilir. Ping: {(pingMs.HasValue ? $"{pingMs.Value}ms" : "—")}, RDP: {(rdpOpen ? "açık" : "kapalı")}",
                    HealthBadge.Offline => "Sunucu şu an erişilemiyor.",
                    _ => $"Durum: {badge}. Ping: {(pingMs.HasValue ? $"{pingMs.Value}ms" : "—")}"
                };

            return new List<TimelineEvent>
            {
                new(DateTime.Now, "System", level, message, "📊")
            };
        }

        private static List<DetectedComponent> BuildHealthComponents(
            HealthBadge badge,
            bool rdpOpen,
            long? pingMs,
            WmiSnapshot metrics)
        {
            var components = new List<DetectedComponent>
            {
                new("Health", "Status", badge.ToString(), badge == HealthBadge.Healthy ? "✅" : "⚠️"),
                new("RDP", "Port", rdpOpen ? "Open" : "Closed", rdpOpen ? "🟢" : "🔴")
            };

            if (pingMs.HasValue)
                components.Add(new("Latency", "Network", $"{pingMs.Value}ms", pingMs < 100 ? "⚡" : "🐢"));

            if (metrics.Connected)
                components.Add(new("WMI", "Agent", "Connected", "🛰️"));

            return components;
        }

        private static WmiSnapshot CloneMetrics(WmiSnapshot source) => new()
        {
            OSName = source.OSName,
            CPUModel = source.CPUModel,
            CPUCoreCount = source.CPUCoreCount,
            TotalRamGb = source.TotalRamGb,
            DomainName = source.DomainName,
            IPAddresses = source.IPAddresses?.ToList() ?? new List<string>(),
            DiskUsedPct = source.DiskUsedPct,
            DiskFreeGb = source.DiskFreeGb,
            DiskTotalGb = source.DiskTotalGb,
            CpuPct = source.CpuPct,
            RamUsedPct = source.RamUsedPct,
            RamFreeGb = source.RamFreeGb,
            Uptime = source.Uptime,
            ActiveUsers = source.ActiveUsers,
            Connected = source.Connected,
            Compatibility = source.Compatibility,
            NetworkAdapter = source.NetworkAdapter,
            LastUser = source.LastUser,
            LastLogon = source.LastLogon,
            LastSession = source.LastSession,
            Services = source.Services?.ToList() ?? new List<ServiceRow>(),
            Sessions = source.Sessions?.ToList() ?? new List<SessionRow>(),
            Timeline = source.Timeline?.ToList() ?? new List<TimelineEvent>(),
            CollectedAt = source.CollectedAt,
            PingMs = source.PingMs,
            Ip = source.Ip,
            Username = source.Username,
            DataSource = source.DataSource
        };

        private static bool HasNonSystemTimeline(List<TimelineEvent> timeline) =>
            timeline.Any(e => !string.Equals(e.Type, "System", StringComparison.OrdinalIgnoreCase));
    }
}
