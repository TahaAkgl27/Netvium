using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    /// <summary>
    /// Never overwrite good snapshot data with null/empty/unknown live values.
    /// </summary>
    public static class SnapshotMergeHelper
    {
        public static WmiSnapshot MergeWmi(WmiSnapshot? baseline, WmiSnapshot? incoming)
        {
            if (baseline == null)
                return incoming?.Connected == true ? incoming : incoming ?? new WmiSnapshot { Connected = false };

            if (incoming == null || !incoming.Connected)
            {
                var kept = Clone(baseline);
                kept.Connected = IsFullWmiMetrics(baseline) || HasDisplayableData(baseline);
                if (incoming?.PingMs is >= 0)
                    kept.PingMs = incoming.PingMs;
                return kept;
            }

            if (!IsFullWmiMetrics(incoming) && IsFullWmiMetrics(baseline))
            {
                if (ShouldAcceptIncomingVolatiles(incoming, baseline))
                    return MergeLiveOverBaseline(incoming, baseline);

                var patched = Clone(baseline);
                if (incoming.PingMs >= 0) patched.PingMs = incoming.PingMs;
                if (IsWmiSnapshotNewer(incoming, baseline)) patched.CollectedAt = incoming.CollectedAt;
                return patched;
            }

            var merged = Clone(baseline);
            merged.Connected = true;

            merged.OSName = PickString(incoming.OSName, baseline.OSName);
            merged.CPUModel = PickString(incoming.CPUModel, baseline.CPUModel);
            merged.CPUCoreCount = incoming.CPUCoreCount ?? baseline.CPUCoreCount;
            merged.TotalRamGb = incoming.TotalRamGb ?? baseline.TotalRamGb;
            merged.DomainName = PickString(incoming.DomainName, baseline.DomainName);
            merged.Compatibility = PickString(incoming.Compatibility, baseline.Compatibility);
            merged.NetworkAdapter = PickString(incoming.NetworkAdapter, baseline.NetworkAdapter);
            merged.LastUser = PickString(incoming.LastUser, baseline.LastUser);
            merged.LastLogon = PickString(incoming.LastLogon, baseline.LastLogon);
            merged.LastSession = PickString(incoming.LastSession, baseline.LastSession);
            merged.Error = PickString(incoming.Error, baseline.Error);
            merged.Status = PickString(incoming.Status, baseline.Status) ?? baseline.Status;
            merged.DataSource = PickString(incoming.DataSource, baseline.DataSource) ?? baseline.DataSource;

            merged.DiskUsedPct = PickVolatile(incoming.DiskUsedPct, baseline.DiskUsedPct, incoming, baseline);
            if (incoming.DiskTotalGb > 0) merged.DiskTotalGb = incoming.DiskTotalGb;
            else if (baseline.DiskTotalGb > 0) merged.DiskTotalGb = baseline.DiskTotalGb;
            if (incoming.DiskFreeGb > 0) merged.DiskFreeGb = incoming.DiskFreeGb;
            else if (baseline.DiskFreeGb > 0) merged.DiskFreeGb = baseline.DiskFreeGb;
            merged.CpuPct = PickVolatile(incoming.CpuPct, baseline.CpuPct, incoming, baseline);
            merged.RamUsedPct = PickVolatile(incoming.RamUsedPct, baseline.RamUsedPct, incoming, baseline);
            if (incoming.RamFreeGb > 0) merged.RamFreeGb = incoming.RamFreeGb;
            else if (baseline.RamFreeGb > 0) merged.RamFreeGb = baseline.RamFreeGb;
            merged.Uptime = PickVolatile(incoming.Uptime, baseline.Uptime, incoming, baseline);
            merged.ActiveUsers = PickVolatile(incoming.ActiveUsers, baseline.ActiveUsers, incoming, baseline);
            merged.PingMs = incoming.PingMs >= 0 ? incoming.PingMs : baseline.PingMs;

            merged.IPAddresses = incoming.IPAddresses?.Count > 0 ? incoming.IPAddresses : baseline.IPAddresses;
            merged.ListenPorts = incoming.ListenPorts?.Count > 0 ? incoming.ListenPorts : baseline.ListenPorts;
            merged.SharedFolders = incoming.SharedFolders?.Count > 0 ? incoming.SharedFolders : baseline.SharedFolders;
            merged.Services = incoming.Services?.Count > 0 ? incoming.Services : baseline.Services;
            merged.TopProcesses = incoming.TopProcesses?.Count > 0 ? incoming.TopProcesses : baseline.TopProcesses;
            merged.Sessions = incoming.Sessions?.Count > 0 ? incoming.Sessions : baseline.Sessions;
            merged.Timeline = incoming.Timeline?.Count > 0 ? incoming.Timeline : baseline.Timeline;

            if (IsWmiSnapshotNewer(incoming, baseline))
                merged.CollectedAt = incoming.CollectedAt;
            else
                merged.CollectedAt = baseline.CollectedAt;

            if (!string.IsNullOrWhiteSpace(incoming.Ip))
                merged.Ip = incoming.Ip;
            if (!string.IsNullOrWhiteSpace(incoming.Username))
                merged.Username = incoming.Username;

            return merged;
        }

        public static PersistentServerSnapshot MergePersistent(PersistentServerSnapshot? baseline, PersistentServerSnapshot? incoming)
        {
            if (baseline == null) return incoming ?? new PersistentServerSnapshot();
            if (incoming == null) return baseline;

            var incomingProbe = IsBasicProbeSnapshot(incoming);
            var baselineFull = IsFullSnapshot(baseline);

            if (incomingProbe && baselineFull)
                return PatchBasicProbeOntoFull(baseline, incoming);

            return new PersistentServerSnapshot
            {
                Version = incoming.Version ?? baseline.Version,
                ServerKeyHash = incoming.ServerKeyHash ?? baseline.ServerKeyHash,
                LastUpdatedUtc = incoming.LastUpdatedUtc > baseline.LastUpdatedUtc ? incoming.LastUpdatedUtc : baseline.LastUpdatedUtc,
                SnapshotKind = IsFullSnapshot(incoming) ? SnapshotKinds.FullWmi : baseline.SnapshotKind,
                SectionTimestamps = MergeTimestamps(baseline.SectionTimestamps, incoming.SectionTimestamps),
                Metrics = MergeWmi(baseline.Metrics, incoming.Metrics),
                Services = incoming.Services?.Count > 0 ? incoming.Services : baseline.Services,
                Timeline = PickTimeline(baseline.Timeline, incoming.Timeline, incomingProbe, baselineFull),
                AiInsights = incoming.AiInsights?.Count > 0 ? incoming.AiInsights : baseline.AiInsights,
                RuleInsights = MergeRuleInsights(baseline.RuleInsights, incoming.RuleInsights, incomingProbe),
                SystemStatusComponents = PickComponents(baseline, incoming),
                DetectedRole = IsFullSnapshot(incoming)
                    ? PickString(incoming.DetectedRole, baseline.DetectedRole)
                    : baseline.DetectedRole,
                IntelligenceConfidence = incoming.IntelligenceConfidence > 0 && IsFullSnapshot(incoming)
                    ? incoming.IntelligenceConfidence
                    : baseline.IntelligenceConfidence
            };
        }

        /// <summary>
        /// Disk persist: newer snapshot wins; older data only fills missing fields.
        /// </summary>
        public static PersistentServerSnapshot MergePersistentForDiskSave(
            PersistentServerSnapshot? existing,
            PersistentServerSnapshot incoming)
        {
            if (existing == null) return incoming;
            if (incoming == null) return existing;
            return IsPersistentSnapshotNewer(incoming, existing)
                ? MergePersistent(incoming, existing)
                : MergePersistent(existing, incoming);
        }

        /// <summary>
        /// Pick the fresher WMI snapshot as baseline; enrich from the other.
        /// </summary>
        public static WmiSnapshot MergeWmiPreferNewer(WmiSnapshot? left, WmiSnapshot? right)
        {
            if (left == null) return right ?? new WmiSnapshot { Connected = false };
            if (right == null) return left;
            return IsWmiSnapshotNewer(left, right)
                ? MergeWmi(left, right)
                : MergeWmi(right, left);
        }

        /// <summary>
        /// Live fetch wins volatile metrics; baseline only fills missing structural fields.
        /// </summary>
        public static WmiSnapshot MergeLiveOverBaseline(WmiSnapshot live, WmiSnapshot? baseline)
        {
            if (baseline == null)
                return Clone(live);

            var merged = Clone(live);
            merged.Connected = true;

            merged.OSName = PickString(live.OSName, baseline.OSName);
            merged.CPUModel = PickString(live.CPUModel, baseline.CPUModel);
            merged.CPUCoreCount = live.CPUCoreCount ?? baseline.CPUCoreCount;
            merged.TotalRamGb = live.TotalRamGb ?? baseline.TotalRamGb;
            merged.DomainName = PickString(live.DomainName, baseline.DomainName);
            merged.Compatibility = PickString(live.Compatibility, baseline.Compatibility);
            merged.NetworkAdapter = PickString(live.NetworkAdapter, baseline.NetworkAdapter);
            merged.LastUser = PickString(live.LastUser, baseline.LastUser);
            merged.LastLogon = PickString(live.LastLogon, baseline.LastLogon);
            merged.LastSession = PickString(live.LastSession, baseline.LastSession);
            merged.Error = PickString(live.Error, baseline.Error);
            merged.Status = PickString(live.Status, baseline.Status) ?? baseline.Status;
            merged.DataSource = PickString(live.DataSource, baseline.DataSource) ?? baseline.DataSource;

            merged.DiskUsedPct = live.DiskUsedPct ?? baseline.DiskUsedPct;
            if (live.DiskTotalGb > 0) merged.DiskTotalGb = live.DiskTotalGb;
            else if (baseline.DiskTotalGb > 0) merged.DiskTotalGb = baseline.DiskTotalGb;
            if (live.DiskFreeGb > 0) merged.DiskFreeGb = live.DiskFreeGb;
            else if (baseline.DiskFreeGb > 0) merged.DiskFreeGb = baseline.DiskFreeGb;
            merged.CpuPct = live.CpuPct ?? baseline.CpuPct;
            merged.RamUsedPct = live.RamUsedPct ?? baseline.RamUsedPct;
            if (live.RamFreeGb > 0) merged.RamFreeGb = live.RamFreeGb;
            else if (baseline.RamFreeGb > 0) merged.RamFreeGb = baseline.RamFreeGb;
            merged.Uptime = live.Uptime ?? baseline.Uptime;
            merged.ActiveUsers = live.ActiveUsers ?? baseline.ActiveUsers;
            merged.PingMs = live.PingMs >= 0 ? live.PingMs : baseline.PingMs;

            merged.IPAddresses = live.IPAddresses?.Count > 0 ? live.IPAddresses : baseline.IPAddresses;
            merged.ListenPorts = live.ListenPorts?.Count > 0 ? live.ListenPorts : baseline.ListenPorts;
            merged.SharedFolders = live.SharedFolders?.Count > 0 ? live.SharedFolders : baseline.SharedFolders;
            merged.Services = live.Services?.Count > 0 ? live.Services : baseline.Services;
            merged.TopProcesses = live.TopProcesses?.Count > 0 ? live.TopProcesses : baseline.TopProcesses;
            merged.Sessions = live.Sessions?.Count > 0 ? live.Sessions : baseline.Sessions;
            merged.Timeline = live.Timeline?.Count > 0 ? live.Timeline : baseline.Timeline;

            merged.CollectedAt = live.CollectedAt != default ? live.CollectedAt : baseline.CollectedAt;
            if (string.IsNullOrWhiteSpace(merged.Ip) && !string.IsNullOrWhiteSpace(baseline.Ip))
                merged.Ip = baseline.Ip;
            if (string.IsNullOrWhiteSpace(merged.Username) && !string.IsNullOrWhiteSpace(baseline.Username))
                merged.Username = baseline.Username;

            return merged;
        }

        /// <summary>
        /// Reject cache/disk snapshot volatiles that are clearly older than live session data.
        /// </summary>
        public static bool ShouldAcceptIncomingVolatiles(WmiSnapshot? incoming, WmiSnapshot? baseline)
        {
            if (incoming == null || baseline == null)
                return true;

            if (IsStaleDataSource(incoming) && !IsStaleDataSource(baseline))
                return false;

            if (IsStaleDataSource(incoming) && IsLiveDataSource(baseline))
                return false;

            return ShouldAcceptIncomingVolatilesAgainstBaseline(incoming, baseline);
        }

        private static bool ShouldAcceptIncomingVolatilesAgainstBaseline(WmiSnapshot incoming, WmiSnapshot baseline)
        {
            if (incoming.Uptime.HasValue && baseline.Uptime.HasValue
                && incoming.Uptime.Value > baseline.Uptime.Value + TimeSpan.FromMinutes(10))
                return false;

            if (!IsWmiSnapshotNewer(incoming, baseline)
                && baseline.Uptime.HasValue
                && incoming.Uptime.HasValue
                && incoming.Uptime.Value > baseline.Uptime.Value)
                return false;

            return true;
        }

        public static bool IsCacheDataSource(WmiSnapshot? snap) =>
            snap != null && string.Equals(snap.DataSource, SnapshotDataSources.Cache, StringComparison.OrdinalIgnoreCase);

        public static bool IsStaleDataSource(WmiSnapshot? snap) =>
            snap != null && (
                string.Equals(snap.DataSource, SnapshotDataSources.Cache, StringComparison.OrdinalIgnoreCase)
                || string.Equals(snap.DataSource, SnapshotDataSources.Persistent, StringComparison.OrdinalIgnoreCase));

        public static bool IsLiveDataSource(WmiSnapshot? snap) =>
            snap != null && (
                string.Equals(snap.DataSource, SnapshotDataSources.Wmi, StringComparison.OrdinalIgnoreCase)
                || string.Equals(snap.DataSource, SnapshotDataSources.Agent, StringComparison.OrdinalIgnoreCase));

        private static TimeSpan? PickVolatile(
            TimeSpan? incomingValue,
            TimeSpan? baselineValue,
            WmiSnapshot incoming,
            WmiSnapshot baseline)
        {
            if (!incomingValue.HasValue) return baselineValue;
            if (!baselineValue.HasValue) return incomingValue;
            if (IsWmiSnapshotNewer(incoming, baseline)) return incomingValue;
            if (IsWmiSnapshotNewer(baseline, incoming)) return baselineValue;
            return incomingValue.Value < baselineValue.Value ? incomingValue : baselineValue;
        }

        private static double? PickVolatile(
            double? incomingValue,
            double? baselineValue,
            WmiSnapshot incoming,
            WmiSnapshot baseline)
        {
            if (!incomingValue.HasValue) return baselineValue;
            if (!baselineValue.HasValue) return incomingValue;
            return IsWmiSnapshotNewer(incoming, baseline) ? incomingValue : baselineValue;
        }

        private static int? PickVolatile(
            int? incomingValue,
            int? baselineValue,
            WmiSnapshot incoming,
            WmiSnapshot baseline)
        {
            if (!incomingValue.HasValue) return baselineValue;
            if (!baselineValue.HasValue) return incomingValue;
            return IsWmiSnapshotNewer(incoming, baseline) ? incomingValue : baselineValue;
        }

        public static bool IsWmiSnapshotNewer(WmiSnapshot? candidate, WmiSnapshot? baseline)
        {
            if (candidate == null) return false;
            if (baseline == null) return true;
            var candidateAt = NormalizeCollectedAt(candidate.CollectedAt);
            var baselineAt = NormalizeCollectedAt(baseline.CollectedAt);
            if (candidateAt != baselineAt)
                return candidateAt > baselineAt;
            return candidate.Connected && !baseline.Connected;
        }

        public static bool IsPersistentSnapshotNewer(PersistentServerSnapshot candidate, PersistentServerSnapshot baseline)
        {
            var candidateMetricsAt = NormalizeCollectedAt(candidate.Metrics?.CollectedAt ?? DateTime.MinValue);
            var baselineMetricsAt = NormalizeCollectedAt(baseline.Metrics?.CollectedAt ?? DateTime.MinValue);
            if (candidateMetricsAt != baselineMetricsAt && candidateMetricsAt > DateTime.MinValue)
                return candidateMetricsAt > baselineMetricsAt;

            return candidate.LastUpdatedUtc > baseline.LastUpdatedUtc;
        }

        private static DateTime NormalizeCollectedAt(DateTime value) =>
            value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
                : value.ToUniversalTime();

        public static bool IsFullWmiMetrics(WmiSnapshot? snap)
        {
            if (snap == null || !snap.Connected) return false;
            return IsMeaningfulOs(snap.OSName)
                || snap.CpuPct.HasValue
                || snap.RamUsedPct.HasValue
                || snap.DiskUsedPct.HasValue
                || snap.Services?.Count > 0
                || snap.CPUCoreCount.HasValue
                || snap.TotalRamGb.HasValue;
        }

        public static bool IsFullSnapshot(PersistentServerSnapshot? snap)
        {
            if (snap == null) return false;
            if (string.Equals(snap.SnapshotKind, SnapshotKinds.FullWmi, StringComparison.OrdinalIgnoreCase))
                return true;
            return IsFullWmiMetrics(snap.Metrics) || snap.Services.Count > 0;
        }

        public static bool IsBasicProbeSnapshot(PersistentServerSnapshot? snap)
        {
            if (snap == null) return false;
            if (string.Equals(snap.SnapshotKind, SnapshotKinds.BasicProbe, StringComparison.OrdinalIgnoreCase))
                return !IsFullSnapshot(snap);
            return !IsFullSnapshot(snap);
        }

        public static bool HasSnapshotUiContent(PersistentServerSnapshot? snap)
        {
            if (snap == null) return false;
            return IsFullSnapshot(snap)
                || snap.Services.Count > 0
                || snap.AiInsights.Count > 0
                || IsFullWmiMetrics(snap.Metrics);
        }

        public static bool HasDisplayableData(WmiSnapshot? snap)
        {
            if (snap == null) return false;
            return IsFullWmiMetrics(snap)
                || snap.Services?.Count > 0
                || snap.Timeline?.Count > 0;
        }

        public static bool IsMeaningfulOs(string? value) => IsMeaningful(value);

        private static PersistentServerSnapshot PatchBasicProbeOntoFull(
            PersistentServerSnapshot baseline,
            PersistentServerSnapshot probe)
        {
            var merged = ClonePersistent(baseline);
            merged.Metrics = MergeWmi(baseline.Metrics, probe.Metrics);
            merged.SectionTimestamps = MergeTimestamps(baseline.SectionTimestamps, probe.SectionTimestamps);
            merged.RuleInsights = MergeRuleInsights(baseline.RuleInsights, probe.RuleInsights, incomingProbe: true);
            merged.LastUpdatedUtc = probe.LastUpdatedUtc > baseline.LastUpdatedUtc
                ? probe.LastUpdatedUtc
                : baseline.LastUpdatedUtc;
            return merged;
        }

        private static SnapshotSectionTimestamps MergeTimestamps(
            SnapshotSectionTimestamps? baseline,
            SnapshotSectionTimestamps? incoming)
        {
            baseline ??= new SnapshotSectionTimestamps();
            if (incoming == null) return baseline;
            return new SnapshotSectionTimestamps
            {
                OsInfoUtc = incoming.OsInfoUtc ?? baseline.OsInfoUtc,
                HardwareInfoUtc = incoming.HardwareInfoUtc ?? baseline.HardwareInfoUtc,
                NetworkInfoUtc = incoming.NetworkInfoUtc ?? baseline.NetworkInfoUtc,
                ServicesUtc = incoming.ServicesUtc ?? baseline.ServicesUtc,
                MetricsUtc = incoming.MetricsUtc ?? baseline.MetricsUtc,
                LatencyUtc = incoming.LatencyUtc ?? baseline.LatencyUtc,
                TimelineUtc = incoming.TimelineUtc ?? baseline.TimelineUtc,
                AiAnalysisUtc = incoming.AiAnalysisUtc ?? baseline.AiAnalysisUtc,
                GeneralInfoUtc = incoming.GeneralInfoUtc ?? baseline.GeneralInfoUtc
            };
        }

        private static List<TimelineEvent> PickTimeline(
            List<TimelineEvent> baseline,
            List<TimelineEvent> incoming,
            bool incomingProbe,
            bool baselineFull)
        {
            if (HasNonProbeTimeline(incoming) && (!incomingProbe || !HasNonProbeTimeline(baseline)))
                return incoming;
            if (baseline.Count > 0 && (incomingProbe || incoming.Count == 0))
                return baseline;
            return incoming.Count > 0 ? incoming : baseline;
        }

        private static List<DetectedComponent> PickComponents(
            PersistentServerSnapshot baseline,
            PersistentServerSnapshot incoming)
        {
            if (IsFullSnapshot(incoming) && incoming.SystemStatusComponents?.Count > 0)
                return incoming.SystemStatusComponents;
            if (baseline.SystemStatusComponents?.Count > 0)
                return baseline.SystemStatusComponents;
            return incoming.SystemStatusComponents ?? new List<DetectedComponent>();
        }

        private static List<SmartInsight> MergeRuleInsights(
            List<SmartInsight> baseline,
            List<SmartInsight> incoming,
            bool incomingProbe)
        {
            if (incoming == null || incoming.Count == 0) return baseline;
            if (!incomingProbe) return incoming;
            var merged = baseline.ToList();
            foreach (var ins in incoming)
            {
                if (!merged.Any(x => x.Text == ins.Text))
                    merged.Add(ins);
            }
            return merged;
        }

        private static bool HasNonProbeTimeline(List<TimelineEvent>? timeline) =>
            timeline != null && timeline.Any(e =>
                !string.Equals(e.Type, "System", StringComparison.OrdinalIgnoreCase)
                || (e.Message?.Length ?? 0) > 80);

        private static PersistentServerSnapshot ClonePersistent(PersistentServerSnapshot source) => new()
        {
            Version = source.Version,
            ServerKeyHash = source.ServerKeyHash,
            LastUpdatedUtc = source.LastUpdatedUtc,
            SnapshotKind = source.SnapshotKind,
            SectionTimestamps = source.SectionTimestamps,
            Metrics = Clone(source.Metrics),
            Services = source.Services?.ToList() ?? new List<ServiceRow>(),
            Timeline = source.Timeline?.ToList() ?? new List<TimelineEvent>(),
            AiInsights = source.AiInsights?.ToList() ?? new List<SmartInsight>(),
            RuleInsights = source.RuleInsights?.ToList() ?? new List<SmartInsight>(),
            SystemStatusComponents = source.SystemStatusComponents?.ToList() ?? new List<DetectedComponent>(),
            DetectedRole = source.DetectedRole,
            IntelligenceConfidence = source.IntelligenceConfidence
        };

        private static WmiSnapshot Clone(WmiSnapshot source) => new()
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
            Error = source.Error,
            Status = source.Status,
            Compatibility = source.Compatibility,
            NetworkAdapter = source.NetworkAdapter,
            LastUser = source.LastUser,
            LastLogon = source.LastLogon,
            LastSession = source.LastSession,
            ListenPorts = source.ListenPorts?.ToList() ?? new List<int>(),
            SharedFolders = source.SharedFolders?.ToList() ?? new List<string>(),
            Services = source.Services?.ToList() ?? new List<ServiceRow>(),
            TopProcesses = source.TopProcesses?.ToList() ?? new List<ProcessRow>(),
            Sessions = source.Sessions?.ToList() ?? new List<SessionRow>(),
            Timeline = source.Timeline?.ToList() ?? new List<TimelineEvent>(),
            CollectedAt = source.CollectedAt,
            PingMs = source.PingMs,
            Ip = source.Ip,
            Username = source.Username,
            DataSource = source.DataSource
        };

        private static string? PickString(string? incoming, string? baseline)
        {
            if (IsMeaningful(incoming)) return incoming;
            return baseline;
        }

        private static bool IsMeaningful(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            return !v.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                && !v.Equals("ulaşılamıyor", StringComparison.OrdinalIgnoreCase)
                && !v.Equals("-", StringComparison.Ordinal)
                && !v.Equals("n/a", StringComparison.OrdinalIgnoreCase)
                && !v.Equals("henüz bilinmiyor", StringComparison.OrdinalIgnoreCase);
        }
    }
}
