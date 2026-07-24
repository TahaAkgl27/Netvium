using System;
using System.Linq;
using System.Threading.Tasks;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    public static class ConnectionStatisticsService
    {
        public static ConnectionStatistics Compute(int daysBack = 30)
        {
            try
            {
                var history = ConnectionHistoryService.LoadRecent(maxEntries: 10_000, daysBack: Math.Max(daysBack, 90));
                return ComputeFromHistory(history, daysBack);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to compute connection statistics.", ex, category: "Audit", action: "ConnectionStatistics");
                return new ConnectionStatistics();
            }
        }

        public static async Task<ConnectionStatistics> ComputeAsync(int daysBack = 30)
        {
            try
            {
                var history = await ConnectionHistoryService.LoadRecentAsync(maxEntries: 10_000, daysBack: Math.Max(daysBack, 90))
                    .ConfigureAwait(false);
                return ComputeFromHistory(history, daysBack);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to compute connection statistics.", ex, category: "Audit", action: "ConnectionStatistics");
                return new ConnectionStatistics();
            }
        }

        private static ConnectionStatistics ComputeFromHistory(System.Collections.Generic.IReadOnlyList<ConnectionHistoryEntry> history, int daysBack)
        {
            var cutoff = DateTime.Now.AddDays(-daysBack);

            var connectEntries = history
                .Where(e => string.Equals(e.Action, AuditActions.Connect, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.Action, AuditActions.QuickConnect, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.Action, AuditActions.Reconnect, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var successful = connectEntries.Where(e => e.Success).ToList();
            var failed = connectEntries.Where(e => e.Failed).ToList();

            var serverCounts = successful
                .GroupBy(e => string.IsNullOrWhiteSpace(e.ServerIp) ? e.ServerName : e.ServerIp, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            var durations = connectEntries
                .Where(e => e.DurationMs.HasValue && e.DurationMs.Value > 0)
                .Select(e => e.DurationMs!.Value)
                .ToList();

            var sessionDurations = history
                .Where(e => string.Equals(e.Action, AuditActions.Disconnect, StringComparison.OrdinalIgnoreCase) &&
                            e.DurationMs.HasValue && e.DurationMs.Value > 0)
                .Select(e => e.DurationMs!.Value)
                .ToList();

            var longestSession = sessionDurations.Count > 0
                ? sessionDurations.Max()
                : durations.Count > 0 ? durations.Max() : 0L;

            return new ConnectionStatistics
            {
                MostConnectedServer = serverCounts.FirstOrDefault()?.Key,
                TotalConnections = connectEntries.Count,
                SuccessfulConnections = successful.Count,
                FailedConnections = failed.Count,
                AverageConnectionDurationMs = durations.Count > 0 ? durations.Average() : 0,
                LongestSessionDurationMs = longestSession,
                ConnectionsLast30Days = connectEntries.Count(e => e.Time >= cutoff)
            };
        }
    }
}
