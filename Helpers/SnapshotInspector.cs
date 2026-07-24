using System;
using System.IO;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class SnapshotInspector
    {
        public sealed class InspectResult
        {
            public bool HasGeneralInfo { get; init; }
            public bool HasOsName { get; init; }
            public bool HasMetrics { get; init; }
            public bool HasCpu { get; init; }
            public bool HasRam { get; init; }
            public bool HasDisk { get; init; }
            public bool HasServices { get; init; }
            public int ServicesCount { get; init; }
            public bool HasTimeline { get; init; }
            public int TimelineCount { get; init; }
            public bool HasAi { get; init; }
            public bool IsBasicProbe { get; init; }
            public bool IsFullWmi { get; init; }
            public string Source { get; init; } = "";
            public DateTime? LastUpdatedUtc { get; init; }
        }

        public static InspectResult Inspect(PersistentServerSnapshot? snapshot, string source)
        {
            if (snapshot == null)
            {
                return new InspectResult { Source = source };
            }

            var m = snapshot.Metrics;
            var isFull = SnapshotMergeHelper.IsFullWmiMetrics(m)
                || string.Equals(snapshot.SnapshotKind, SnapshotKinds.FullWmi, StringComparison.OrdinalIgnoreCase);
            var isProbe = string.Equals(snapshot.SnapshotKind, SnapshotKinds.BasicProbe, StringComparison.OrdinalIgnoreCase)
                || (!isFull && (m?.PingMs >= 0 || snapshot.RuleInsights.Count > 0));

            return new InspectResult
            {
                HasGeneralInfo = !string.IsNullOrWhiteSpace(snapshot.DetectedRole) || snapshot.RuleInsights.Count > 0,
                HasOsName = SnapshotMergeHelper.IsMeaningfulOs(m?.OSName),
                HasMetrics = m?.CpuPct.HasValue == true || m?.RamUsedPct.HasValue == true || m?.DiskUsedPct.HasValue == true,
                HasCpu = m?.CpuPct.HasValue == true,
                HasRam = m?.RamUsedPct.HasValue == true,
                HasDisk = m?.DiskUsedPct.HasValue == true,
                HasServices = snapshot.Services.Count > 0 || m?.Services?.Count > 0,
                ServicesCount = snapshot.Services.Count > 0 ? snapshot.Services.Count : m?.Services?.Count ?? 0,
                HasTimeline = snapshot.Timeline.Count > 0,
                TimelineCount = snapshot.Timeline.Count,
                HasAi = snapshot.AiInsights.Count > 0,
                IsBasicProbe = isProbe && !isFull,
                IsFullWmi = isFull,
                Source = source,
                LastUpdatedUtc = snapshot.LastUpdatedUtc
            };
        }

        public static string FormatInspectLine(string keyHashPrefix, PersistentServerSnapshot? snapshot, string source)
        {
            var r = Inspect(snapshot, source);
            return $"Snapshot inspect: key=#{keyHashPrefix}, source={r.Source}, full={r.IsFullWmi}, basicProbe={r.IsBasicProbe}, " +
                   $"os={r.HasOsName}, metrics={r.HasMetrics}, cpu={r.HasCpu}, ram={r.HasRam}, disk={r.HasDisk}, " +
                   $"services={r.ServicesCount}, ai={r.HasAi}, timeline={r.TimelineCount}";
        }

        public static void LogInspect(string serverKey, PersistentServerSnapshot? snapshot, string source)
        {
            var hash = Services.PersistentSnapshotStore.HashKey(serverKey);
            var prefix = hash.Length >= 12 ? hash[..12] : hash;
            AppLogger.Debug(FormatInspectLine(prefix, snapshot, source), category: "Snapshot", action: "Inspect");
        }

        public static void InspectAllDiskSnapshots()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Netvium", "snapshots");
                if (!Directory.Exists(dir)) return;

                foreach (var path in Directory.GetFiles(dir, "*.nsnap"))
                {
                    try
                    {
                        var content = File.ReadAllText(path);
                        var json = content.StartsWith("ensnap:", StringComparison.Ordinal)
                            ? CredentialProtector.Unprotect(content.Substring(7))
                            : content;
                        var snap = System.Text.Json.JsonSerializer.Deserialize<PersistentServerSnapshot>(json);
                        var hash = Path.GetFileNameWithoutExtension(path);
                        var prefix = hash.Length >= 12 ? hash[..12] : hash;
                        AppLogger.Debug(FormatInspectLine(prefix, snap, "DiskAudit"), category: "Snapshot", action: "InspectAll");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning("Snapshot disk audit skipped one file.", ex, category: "Snapshot", action: "InspectAll");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Snapshot disk audit failed.", ex, category: "Snapshot", action: "InspectAll");
            }
        }
    }

    public static class SnapshotKinds
    {
        public const string FullWmi = "FullWmi";
        public const string BasicProbe = "BasicProbe";
    }
}
