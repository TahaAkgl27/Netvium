using System;
using System.Collections.Generic;
using System.Linq;

namespace Netvium.Services
{
    /// <summary>
    /// Prevents overlapping WMI / EventLog / AI / sidebar work per server.
    /// </summary>
    public sealed class ServerRefreshCoordinator
    {
        public enum RefreshPhase
        {
            SnapshotLoad = 0,
            FastMetrics = 1,
            DeepServices = 2,
            EventLog = 3,
            Ai = 4
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, HashSet<RefreshPhase>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
        private string? _activeServerKey;

        public void SetActiveServer(string? serverKey) => _activeServerKey = serverKey;

        public bool IsActiveServer(string serverKey) =>
            !string.IsNullOrEmpty(_activeServerKey) &&
            string.Equals(_activeServerKey, serverKey, StringComparison.OrdinalIgnoreCase);

        public bool TryBegin(string serverKey, RefreshPhase phase)
        {
            lock (_gate)
            {
                if (!_inFlight.TryGetValue(serverKey, out var phases))
                {
                    phases = new HashSet<RefreshPhase>();
                    _inFlight[serverKey] = phases;
                }

                if (phases.Contains(phase))
                    return false;

                phases.Add(phase);
                return true;
            }
        }

        public void End(string serverKey, RefreshPhase phase)
        {
            lock (_gate)
            {
                if (!_inFlight.TryGetValue(serverKey, out var phases))
                    return;

                phases.Remove(phase);
                if (phases.Count == 0)
                    _inFlight.Remove(serverKey);
            }
        }

        public bool IsServerRefreshActive(string? serverKey = null)
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(serverKey))
                    return _inFlight.Count > 0;

                return _inFlight.TryGetValue(serverKey, out var phases) && phases.Count > 0;
            }
        }

        public bool ShouldDeferSidebarScan()
        {
            lock (_gate)
            {
                return _inFlight.Values.Any(phases =>
                    phases.Contains(RefreshPhase.FastMetrics) ||
                    phases.Contains(RefreshPhase.DeepServices) ||
                    phases.Contains(RefreshPhase.EventLog) ||
                    phases.Contains(RefreshPhase.Ai));
            }
        }

        public void CancelServer(string serverKey)
        {
            lock (_gate)
            {
                _inFlight.Remove(serverKey);
            }
        }
    }
}
