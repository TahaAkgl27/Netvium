using System;
using Netvium.Helpers.Logging;

namespace Netvium.Helpers
{
    /// <summary>
    /// Guarantees progress UI completes for each dashboard refresh cycle.
    /// </summary>
    public sealed class DashboardRefreshLifecycle
    {
        private readonly LiveRefreshIndicatorController _indicator;
        private string? _activeKey;
        private int _generation;

        public DashboardRefreshLifecycle(LiveRefreshIndicatorController indicator)
        {
            _indicator = indicator;
        }

        public int Begin(string serverKey)
        {
            _generation++;
            _activeKey = serverKey;
            _indicator.Begin(serverKey);
            RefreshDebugLog.Started(serverKey);
            return _generation;
        }

        public void Report(int generation, string serverKey, LiveRefreshMilestone milestone)
        {
            if (!IsCurrent(generation, serverKey))
                return;

            switch (milestone)
            {
                case LiveRefreshMilestone.FastMetrics:
                    RefreshDebugLog.FastMetricsCompleted(serverKey);
                    break;
                case LiveRefreshMilestone.Services:
                    RefreshDebugLog.ServicesCompleted(serverKey);
                    break;
                case LiveRefreshMilestone.EventLog:
                    RefreshDebugLog.EventLogCompleted(serverKey);
                    break;
                case LiveRefreshMilestone.SmartAnalysis:
                    RefreshDebugLog.AiUpdated(serverKey);
                    break;
            }

            _indicator.Report(serverKey, milestone);
        }

        public void MarkAiSkipped(int generation, string serverKey, string reason)
        {
            if (!IsCurrent(generation, serverKey))
                return;
            RefreshDebugLog.AiSkipped(serverKey, reason);
            _indicator.MarkAiSkipped(serverKey);
        }

        public void MarkAiFinished(int generation, string serverKey)
        {
            if (!IsCurrent(generation, serverKey))
                return;
            RefreshDebugLog.AiUpdated(serverKey);
            _indicator.NotifyAiFinished(serverKey);
        }

        public void MarkSnapshotSaved(int generation, string serverKey)
        {
            if (!IsCurrent(generation, serverKey))
                return;
            RefreshDebugLog.SnapshotSaved(serverKey);
            _indicator.NotifySnapshotSaved(serverKey);
        }

        public void CompleteSuccess(int generation, string serverKey)
        {
            if (!IsCurrent(generation, serverKey))
                return;
            _indicator.MarkCompleted(serverKey);
            RefreshDebugLog.ProgressCompleted(serverKey);
        }

        public void CompleteFailed(int generation, string serverKey)
        {
            if (!IsCurrent(generation, serverKey))
                return;
            _indicator.MarkFailed(serverKey);
            RefreshDebugLog.ProgressFailed(serverKey);
        }

        public void Cancel(string? serverKey = null)
        {
            if (!string.IsNullOrEmpty(serverKey)
                && !string.Equals(_activeKey, serverKey, StringComparison.OrdinalIgnoreCase))
                return;

            _activeKey = null;
            _generation++;
            _indicator.MarkCancelled(serverKey);
        }

        public bool IsCurrent(int generation, string serverKey) =>
            generation == _generation
            && !string.IsNullOrEmpty(_activeKey)
            && string.Equals(_activeKey, serverKey, StringComparison.OrdinalIgnoreCase);
    }
}
