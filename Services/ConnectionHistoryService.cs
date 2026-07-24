using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    public static class ConnectionHistoryService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private static readonly HashSet<string> ConnectActions = new(StringComparer.OrdinalIgnoreCase)
        {
            AuditActions.Connect,
            AuditActions.QuickConnect,
            AuditActions.Reconnect
        };

        private static readonly object CacheGate = new();
        private static IReadOnlyList<ConnectionHistoryEntry>? _cachedEntries;
        private static DateTime _cacheExpiryUtc = DateTime.MinValue;
        private static int _cachedMaxEntries;
        private static int _cachedDaysBack;

        public static IReadOnlyList<ConnectionHistoryEntry> LoadRecent(int maxEntries = 200, int daysBack = 90)
        {
            lock (CacheGate)
            {
                if (_cachedEntries != null &&
                    DateTime.UtcNow < _cacheExpiryUtc &&
                    _cachedMaxEntries == maxEntries &&
                    _cachedDaysBack == daysBack)
                {
                    return _cachedEntries;
                }
            }

            var entries = LoadRecentCore(maxEntries, daysBack);
            lock (CacheGate)
            {
                _cachedEntries = entries;
                _cacheExpiryUtc = DateTime.UtcNow.AddMinutes(1);
                _cachedMaxEntries = maxEntries;
                _cachedDaysBack = daysBack;
            }

            return entries;
        }

        public static Task<IReadOnlyList<ConnectionHistoryEntry>> LoadRecentAsync(int maxEntries = 200, int daysBack = 90)
        {
            return Task.Run(() => LoadRecent(maxEntries, daysBack));
        }

        public static void InvalidateCache()
        {
            lock (CacheGate)
            {
                _cachedEntries = null;
                _cacheExpiryUtc = DateTime.MinValue;
            }
        }

        private static List<ConnectionHistoryEntry> LoadRecentCore(int maxEntries, int daysBack)
        {
            var entries = new List<ConnectionHistoryEntry>();
            try
            {
                var auditEntries = ReadAuditEntries(daysBack);
                var byCorrelation = auditEntries
                    .Where(e => !string.IsNullOrWhiteSpace(e.CorrelationId))
                    .GroupBy(e => e.CorrelationId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Timestamp).ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var group in byCorrelation.Values)
                {
                    var started = group.FirstOrDefault(e =>
                        ConnectActions.Contains(e.Action) &&
                        string.Equals(e.Result, AuditResults.Started, StringComparison.OrdinalIgnoreCase));
                    if (started == null)
                        continue;

                    var completed = group.LastOrDefault(e =>
                        ConnectActions.Contains(e.Action) &&
                        !string.Equals(e.Result, AuditResults.Started, StringComparison.OrdinalIgnoreCase));

                    if (completed == null)
                        continue;

                    var success = string.Equals(completed.Result, AuditResults.Success, StringComparison.OrdinalIgnoreCase);
                    var failed = string.Equals(completed.Result, AuditResults.Failed, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(completed.Result, AuditResults.Cancelled, StringComparison.OrdinalIgnoreCase);

                    entries.Add(new ConnectionHistoryEntry
                    {
                        ServerName = started.ServerName ?? completed.ServerName ?? started.ServerIP ?? "Unknown",
                        ServerIp = started.ServerIP ?? completed.ServerIP ?? string.Empty,
                        Time = started.Timestamp.ToLocalTime(),
                        DurationMs = completed.DurationMs,
                        Success = success,
                        Failed = failed,
                        DisconnectReason = failed ? completed.ErrorMessage : null,
                        Action = started.Action,
                        SessionId = completed.SessionId ?? started.SessionId,
                        CorrelationId = started.CorrelationId
                    });
                }

                AppendDisconnectSessions(auditEntries, entries);
                return entries
                    .OrderByDescending(e => e.Time)
                    .Take(maxEntries)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load connection history.", ex, category: "Audit", action: "ConnectionHistory");
                return entries;
            }
        }

        private static void AppendDisconnectSessions(List<AuditLogEntry> auditEntries, List<ConnectionHistoryEntry> entries)
        {
            var connectBySession = auditEntries
                .Where(e => ConnectActions.Contains(e.Action) &&
                            string.Equals(e.Result, AuditResults.Success, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(e.SessionId))
                .GroupBy(e => e.SessionId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Timestamp).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var disconnect in auditEntries.Where(e =>
                         string.Equals(e.Action, AuditActions.Disconnect, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(e.Result, AuditResults.Started, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(disconnect.SessionId))
                    continue;

                if (!connectBySession.TryGetValue(disconnect.SessionId, out var connect))
                    continue;

                var sessionDuration = (long)Math.Max(0, (disconnect.Timestamp - connect.Timestamp).TotalMilliseconds);
                var failed = string.Equals(disconnect.Result, AuditResults.Failed, StringComparison.OrdinalIgnoreCase);

                entries.Add(new ConnectionHistoryEntry
                {
                    ServerName = disconnect.ServerName ?? connect.ServerName ?? disconnect.ServerIP ?? "Unknown",
                    ServerIp = disconnect.ServerIP ?? connect.ServerIP ?? string.Empty,
                    Time = disconnect.Timestamp.ToLocalTime(),
                    DurationMs = sessionDuration,
                    Success = string.Equals(disconnect.Result, AuditResults.Success, StringComparison.OrdinalIgnoreCase),
                    Failed = failed,
                    DisconnectReason = disconnect.ErrorMessage,
                    Action = AuditActions.Disconnect,
                    SessionId = disconnect.SessionId,
                    CorrelationId = disconnect.CorrelationId
                });
            }
        }

        private static List<AuditLogEntry> ReadAuditEntries(int daysBack)
        {
            var result = new List<AuditLogEntry>();
            var dir = AuditLogService.GetAuditDirectory();
            if (!Directory.Exists(dir))
                return result;

            var cutoff = DateTime.UtcNow.AddDays(-daysBack);
            foreach (var file in Directory.GetFiles(dir, "audit_*.jsonl"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff && info.CreationTimeUtc < cutoff)
                        continue;

                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        try
                        {
                            var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, JsonOpts);
                            if (entry != null && entry.Timestamp >= cutoff)
                                result.Add(entry);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Skipping corrupt audit line in {file}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Could not read audit file {file}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
