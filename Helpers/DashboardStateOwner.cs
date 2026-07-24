using System;
using System.Collections.Generic;
using Netvium.Models;

namespace Netvium.Helpers
{
    /// <summary>
    /// Authoritative owner for active dashboard snapshot/live state.
    /// Snapshot is bootstrap-only; after first valid live telemetry, live owns the session.
    /// </summary>
    internal sealed class DashboardStateOwner
    {
        private readonly HashSet<string> _sessionLiveOwnedKeys = new(StringComparer.OrdinalIgnoreCase);

        public string? ActiveServerKey { get; private set; }
        public WmiSnapshot? CurrentSnapshot { get; private set; }
        public LiveDashboardMetrics? CurrentLiveMetrics { get; private set; }
        public bool IsLiveOwned { get; private set; }
        public DateTime? LastLiveUtc { get; private set; }
        public SnapshotSectionTimestamps SectionTimestamps { get; private set; } = new();
        public int RefreshGeneration { get; private set; }
        public string? ActiveLiveMetricsKey { get; private set; }

        public bool IsLiveMetricsActive(string? serverKey) =>
            IsLiveOwned
            || IsSessionLiveOwned(serverKey)
            || (!string.IsNullOrEmpty(serverKey)
                && !string.IsNullOrEmpty(ActiveLiveMetricsKey)
                && string.Equals(ActiveLiveMetricsKey, serverKey, StringComparison.OrdinalIgnoreCase));

        public bool IsActiveServer(string? serverKey) =>
            !string.IsNullOrEmpty(serverKey)
            && !string.IsNullOrEmpty(ActiveServerKey)
            && string.Equals(ActiveServerKey, serverKey, StringComparison.OrdinalIgnoreCase);

        public bool IsSessionLiveOwned(string? serverKey) =>
            !string.IsNullOrEmpty(serverKey) && _sessionLiveOwnedKeys.Contains(serverKey);

        public bool IsLiveTelemetryAuthoritative(string? serverKey) =>
            IsSessionLiveOwned(serverKey);

        public bool IsCurrentGeneration(int generation) => generation == RefreshGeneration;

        public bool ShouldReject(string? serverKey, int? generation = null) =>
            !IsActiveServer(serverKey)
            || (generation.HasValue && generation.Value != RefreshGeneration);

        public void ActivateServer(string serverKey, int refreshGeneration)
        {
            if (!IsActiveServer(serverKey))
            {
                ActiveServerKey = serverKey;
                CurrentSnapshot = null;
                CurrentLiveMetrics = null;
                IsLiveOwned = false;
                LastLiveUtc = null;
                SectionTimestamps = new SnapshotSectionTimestamps();
            }

            RefreshGeneration = refreshGeneration;
            IsLiveOwned = IsSessionLiveOwned(serverKey);
        }

        public void ClearActiveSession()
        {
            ActiveServerKey = null;
            CurrentSnapshot = null;
            CurrentLiveMetrics = null;
            IsLiveOwned = false;
            LastLiveUtc = null;
            RefreshGeneration = 0;
            ActiveLiveMetricsKey = null;
        }

        public void OnServerSwitch(string newServerKey)
        {
            ActiveServerKey = newServerKey;
            CurrentSnapshot = null;
            CurrentLiveMetrics = null;
            IsLiveOwned = IsSessionLiveOwned(newServerKey);
            LastLiveUtc = null;
            ActiveLiveMetricsKey = null;
        }

        public void SetActiveLiveRefresh(string? serverKey) => ActiveLiveMetricsKey = serverKey;

        public void ClearActiveLiveRefresh() => ActiveLiveMetricsKey = null;

        public void SetSectionTimestamps(SnapshotSectionTimestamps timestamps) =>
            SectionTimestamps = timestamps ?? new SnapshotSectionTimestamps();

        public void SetCurrentLiveMetrics(LiveDashboardMetrics? metrics)
        {
            if (metrics == null)
            {
                CurrentLiveMetrics = null;
                return;
            }

            if (IsActiveServer(metrics.ServerKey))
                CurrentLiveMetrics = metrics;
        }

        /// <summary>Bootstrap/cache metrics before first live telemetry.</summary>
        public bool TryApplyBootstrapSnapshot(string serverKey, WmiSnapshot snap, SnapshotSectionTimestamps? timestamps = null)
        {
            if (ShouldReject(serverKey) || IsLiveOwned || IsSessionLiveOwned(serverKey))
                return false;

            CurrentSnapshot = snap;
            if (timestamps != null)
                SectionTimestamps = timestamps;
            return true;
        }

        /// <summary>Persistent disk snapshot for first paint only.</summary>
        public bool TryApplyPersistentSnapshot(string serverKey, WmiSnapshot snap, SnapshotSectionTimestamps? timestamps = null)
        {
            if (ShouldReject(serverKey) || IsLiveOwned || IsSessionLiveOwned(serverKey))
                return false;

            CurrentSnapshot = snap;
            if (timestamps != null)
                SectionTimestamps = timestamps;
            return true;
        }

        /// <summary>Restore previously live-owned server state on re-select (warm session).</summary>
        public bool TryRestoreLiveOwnedSnapshot(string serverKey, WmiSnapshot snap, SnapshotSectionTimestamps? timestamps = null)
        {
            if (ShouldReject(serverKey) || !IsSessionLiveOwned(serverKey))
                return false;

            CurrentSnapshot = snap;
            IsLiveOwned = true;
            if (timestamps != null)
                SectionTimestamps = timestamps;
            return true;
        }

        /// <summary>First valid live telemetry claims ownership for the session.</summary>
        public bool TryApplyLiveSnapshot(string serverKey, int generation, WmiSnapshot liveSnap)
        {
            if (ShouldReject(serverKey, generation))
                return false;

            if (liveSnap == null || !liveSnap.Connected)
                return false;

            MergeIncomingLive(liveSnap, allowStaleVolatileRejection: true);

            if (HasValidLiveVolatiles(liveSnap))
                MarkSessionLiveOwned(serverKey);

            return true;
        }

        /// <summary>Deep-phase enrichment must not downgrade live KPI metrics.</summary>
        public bool TryApplyDeepEnrichment(string serverKey, int generation, WmiSnapshot incoming)
        {
            if (ShouldReject(serverKey, generation) || incoming == null)
                return false;

            if (!IsLiveOwned && !IsSessionLiveOwned(serverKey))
                return TryApplyBootstrapSnapshot(serverKey, incoming);

            MergeIncomingLive(incoming, allowStaleVolatileRejection: true);
            return true;
        }

        public WmiSnapshot? ResolveForUi(string serverKey, WmiSnapshot? incoming)
        {
            if (ShouldReject(serverKey))
                return CurrentSnapshot;

            if (incoming == null || !incoming.Connected)
                return CurrentSnapshot ?? incoming;

            if (IsLiveOwned || IsSessionLiveOwned(serverKey))
                return MergeIncomingForUi(incoming);

            return ResolvePreLive(incoming);
        }

        public WmiSnapshot? ApplyIncomingSnapshotForUi(string serverKey, WmiSnapshot? incoming)
        {
            if (incoming == null || !incoming.Connected)
                return CurrentSnapshot ?? incoming;

            if (ShouldReject(serverKey))
                return CurrentSnapshot ?? incoming;

            if (IsLiveOwned || IsSessionLiveOwned(serverKey))
                return MergeIncomingForUi(incoming);

            if (!IsActiveServer(serverKey))
                return ResolvePreLive(incoming);

            if (CurrentSnapshot == null || !SnapshotMergeHelper.HasDisplayableData(CurrentSnapshot))
                return incoming;

            if (!SnapshotMergeHelper.ShouldAcceptIncomingVolatiles(incoming, CurrentSnapshot))
                return CurrentSnapshot;

            return SnapshotMergeHelper.MergeLiveOverBaseline(incoming, CurrentSnapshot);
        }

        public void CommitUiSnapshot(string serverKey, WmiSnapshot? snap, bool fromLivePath)
        {
            if (ShouldReject(serverKey) || snap == null)
                return;

            if (fromLivePath || IsLiveOwned || IsSessionLiveOwned(serverKey))
            {
                MergeIncomingLive(snap, allowStaleVolatileRejection: true);
                if (HasValidLiveVolatiles(snap))
                    MarkSessionLiveOwned(serverKey);
                return;
            }

            if (!IsLiveOwned && !IsSessionLiveOwned(serverKey))
                CurrentSnapshot = snap;
        }

        public void TouchMetricsSections()
        {
            var now = DateTime.UtcNow;
            SectionTimestamps.MetricsUtc = now;
            SectionTimestamps.LatencyUtc = now;
            SectionTimestamps.ServicesUtc = now;
            SectionTimestamps.OsInfoUtc = now;
            SectionTimestamps.GeneralInfoUtc = now;
            SectionTimestamps.NetworkInfoUtc = now;
            SectionTimestamps.HardwareInfoUtc = now;
        }

        public void TouchTimelineSection() => SectionTimestamps.TimelineUtc = DateTime.UtcNow;

        public void TouchAiSection() => SectionTimestamps.AiAnalysisUtc = DateTime.UtcNow;

        public void ClearAiSection() => SectionTimestamps.AiAnalysisUtc = null;

        public void MarkSessionLiveOwned(string serverKey)
        {
            if (string.IsNullOrEmpty(serverKey))
                return;

            _sessionLiveOwnedKeys.Add(serverKey);
            IsLiveOwned = true;
            ActiveServerKey = serverKey;
            ActiveLiveMetricsKey = serverKey;
            LastLiveUtc = DateTime.UtcNow;
        }

        private static bool HasValidLiveVolatiles(WmiSnapshot snap) =>
            snap.Connected
            && (snap.CpuPct.HasValue || snap.RamUsedPct.HasValue || snap.DiskUsedPct.HasValue);

        private WmiSnapshot? MergeIncomingForUi(WmiSnapshot incoming)
        {
            if (CurrentSnapshot == null || !SnapshotMergeHelper.HasDisplayableData(CurrentSnapshot))
                return incoming;

            if (!SnapshotMergeHelper.ShouldAcceptIncomingVolatiles(incoming, CurrentSnapshot))
                return CurrentSnapshot;

            return SnapshotMergeHelper.MergeLiveOverBaseline(incoming, CurrentSnapshot);
        }

        private WmiSnapshot? ResolvePreLive(WmiSnapshot? incoming)
        {
            if (incoming != null && incoming.Connected)
            {
                if (CurrentSnapshot != null && SnapshotMergeHelper.HasDisplayableData(CurrentSnapshot))
                    return SnapshotMergeHelper.MergeWmiPreferNewer(incoming, CurrentSnapshot);
                return incoming;
            }

            if (CurrentSnapshot != null && SnapshotMergeHelper.HasDisplayableData(CurrentSnapshot))
                return CurrentSnapshot;

            return incoming;
        }

        private void MergeIncomingLive(WmiSnapshot incoming, bool allowStaleVolatileRejection)
        {
            if (CurrentSnapshot == null || !SnapshotMergeHelper.HasDisplayableData(CurrentSnapshot))
            {
                CurrentSnapshot = incoming;
                return;
            }

            if (allowStaleVolatileRejection
                && !SnapshotMergeHelper.ShouldAcceptIncomingVolatiles(incoming, CurrentSnapshot))
                return;

            CurrentSnapshot = SnapshotMergeHelper.MergeLiveOverBaseline(incoming, CurrentSnapshot);
        }
    }
}
