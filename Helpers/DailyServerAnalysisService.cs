using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Models;

namespace Netvium.Helpers
{
    public sealed class DailyServerAnalysisService
    {
        private const int MaxParallelProbes = 5;

        public sealed class ServerProbeInput
        {
            public ServerInfo Server { get; init; } = null!;
            public WmiSnapshot? CachedSnapshot { get; init; }
        }

        public sealed class AnalysisCallbacks
        {
            public Action<string, double>? OnStage { get; init; }
            public Func<ServerInfo, HealthBadge, WmiSnapshot?, long?, bool, string?, Task>? OnServerAnalyzed { get; init; }
            public Func<ServerInfo, CancellationToken, Task<WmiSnapshot?>>? TryRefreshSnapshot { get; init; }
            public Func<string, int, Task<long?>> PingAsync { get; init; } = (_, _) => Task.FromResult<long?>(null);
            public Func<string, int, int, Task<bool>> TcpOpenAsync { get; init; } = (_, _, _) => Task.FromResult(false);
        }

        public async Task<DailyAnalysisReport> AnalyzeAsync(
            IReadOnlyList<ServerProbeInput> servers,
            AnalysisCallbacks callbacks,
            CancellationToken ct = default)
        {
            ReportStage(callbacks, "Sunucular okunuyor...", 5);

            if (servers.Count == 0)
                return DailyAnalysisReport.NoServers();

            var total = servers.Count;
            var findings = new List<(ServerInfo Server, HealthBadge Badge, WmiSnapshot? Snap, long? PingMs, bool RdpOpen, string? Reason, bool HasLive)>();
            var findingsLock = new object();
            var completed = 0;
            using var semaphore = new SemaphoreSlim(MaxParallelProbes);

            var probeTasks = servers.Select(input => ProbeServerAsync(
                input, callbacks, ct, semaphore, findings, findingsLock, total, () =>
                {
                    var done = Interlocked.Increment(ref completed);
                    var progress = 8 + (62.0 * done / total);
                    ReportStage(callbacks, $"Durumlar kontrol ediliyor... ({done}/{total}) {input.Server.Name}", progress);
                }));

            await Task.WhenAll(probeTasks);

            ReportStage(callbacks, "Riskler değerlendiriliyor...", 75);

            if (!findings.Any(f => f.HasLive))
                return DailyAnalysisReport.NoLiveData(total);

            ReportStage(callbacks, "Özet hazırlanıyor...", 90);

            var statuses = findings.Select(f => f.Badge).ToList();
            var (healthy, warning, critical, offline) = ServerHealthEvaluator.CountStatuses(statuses);

            var criticalFindings = new List<DailyServerFinding>();
            var warningFindings = new List<DailyServerFinding>();

            foreach (var item in findings.OrderBy(f => f.Server.Name, StringComparer.OrdinalIgnoreCase))
            {
                var finding = BuildFinding(item.Server, item.Badge, item.Snap, item.PingMs, item.RdpOpen, item.Reason, item.HasLive);
                if (item.Badge is HealthBadge.Critical or HealthBadge.Offline)
                    criticalFindings.Add(finding);
                else if (item.Badge == HealthBadge.Warning)
                    warningFindings.Add(finding);
            }

            var recommendations = BuildRecommendations(criticalFindings, warningFindings, findings);

            ReportStage(callbacks, "Özet hazırlanıyor...", 100);

            return new DailyAnalysisReport
            {
                TotalServers = total,
                HealthyCount = healthy,
                WarningCount = warning,
                CriticalCount = critical,
                OfflineCount = offline,
                HasAnyLiveData = true,
                CriticalFindings = criticalFindings,
                WarningFindings = warningFindings,
                Recommendations = recommendations
            };
        }

        private static async Task ProbeServerAsync(
            ServerProbeInput input,
            AnalysisCallbacks callbacks,
            CancellationToken ct,
            SemaphoreSlim semaphore,
            List<(ServerInfo Server, HealthBadge Badge, WmiSnapshot? Snap, long? PingMs, bool RdpOpen, string? Reason, bool HasLive)> findings,
            object findingsLock,
            int total,
            Action onCompleted)
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var server = input.Server;
                var port = server.Port <= 0 ? 3389 : server.Port;

                var pingMs = await callbacks.PingAsync(server.Ip, 1200);
                var rdpOpen = await callbacks.TcpOpenAsync(server.Ip, port, 1200);

                WmiSnapshot? snap = input.CachedSnapshot;
                var hasLive = pingMs.HasValue || rdpOpen || (snap?.Connected == true);

                // Yalnızca önbellek yoksa kısa WMI dene — force/full tarama yok
                if (snap == null
                    && callbacks.TryRefreshSnapshot != null
                    && rdpOpen
                    && !string.IsNullOrWhiteSpace(server.Username))
                {
                    try
                    {
                        var refreshed = await callbacks.TryRefreshSnapshot(server, ct);
                        if (refreshed?.Connected == true)
                        {
                            snap = refreshed;
                            hasLive = true;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Live snapshot refresh failed; continuing without cache.", ex);
                    }
                }

                var (badge, reason) = ServerHealthEvaluator.CalculateOverallHealth(pingMs, rdpOpen, snap);
                var row = (server, badge, snap, pingMs, rdpOpen, reason, hasLive);

                lock (findingsLock)
                    findings.Add(row);

                if (callbacks.OnServerAnalyzed != null)
                    await callbacks.OnServerAnalyzed(server, badge, snap, pingMs, rdpOpen, reason);

                onCompleted();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static void ReportStage(AnalysisCallbacks callbacks, string message, double progress)
            => callbacks.OnStage?.Invoke(message, progress);

        private static DailyServerFinding BuildFinding(
            ServerInfo server,
            HealthBadge badge,
            WmiSnapshot? snap,
            long? pingMs,
            bool rdpOpen,
            string? healthReason,
            bool hasLive)
        {
            var details = BuildDetailLines(server, snap, pingMs, rdpOpen, healthReason);
            var summary = details.Count > 0
                ? string.Join(", ", details)
                : (healthReason ?? "Ek detay yok");

            DateTime? lastSeen = snap?.CollectedAt;
            if (lastSeen == default || lastSeen == DateTime.MinValue)
                lastSeen = null;

            var latency = pingMs.HasValue ? $"{pingMs.Value}ms" : server.Latency;

            return new DailyServerFinding
            {
                ServerName = server.Name,
                Ip = server.Ip,
                Status = badge,
                Summary = summary,
                Details = details,
                LastSeen = lastSeen,
                Latency = string.IsNullOrWhiteSpace(latency) || latency == "N/A" ? null : latency,
                HasLiveData = hasLive
            };
        }

        private static List<string> BuildDetailLines(
            ServerInfo server,
            WmiSnapshot? snap,
            long? pingMs,
            bool rdpOpen,
            string? healthReason)
        {
            var lines = new List<string>();

            if (!rdpOpen)
                lines.Add("RDP kapalı");

            if (pingMs.HasValue)
            {
                if (pingMs.Value >= 150)
                    lines.Add($"Ping yüksek ({pingMs.Value}ms)");
            }
            else if (rdpOpen)
            {
                lines.Add("Ping yanıt vermiyor");
            }

            if (snap?.Connected == true)
            {
                if (snap.CpuPct >= 90)
                    lines.Add($"CPU yüksek (%{snap.CpuPct})");
                else if (server.LastCpu >= 90)
                    lines.Add($"CPU yüksek (%{server.LastCpu:0})");

                if (snap.RamUsedPct >= 85)
                    lines.Add($"RAM yüksek (%{snap.RamUsedPct:0})");
                else if (server.LastRam > 0 && snap.TotalRamGb > 0 && server.LastRam / snap.TotalRamGb * 100 >= 85)
                    lines.Add("RAM yüksek");

                if (snap.DiskUsedPct >= 90)
                    lines.Add($"Disk düşük (%{snap.DiskUsedPct:0})");
                else if (server.LastDisk >= 90)
                    lines.Add($"Disk düşük (%{server.LastDisk:0})");

                var stopped = snap.Services?
                    .Where(s => !s.State.Equals("Running", StringComparison.OrdinalIgnoreCase)
                             && !s.State.Equals("Started", StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(s => $"{s.Display} servisi yanıt vermiyor")
                    .ToList();
                if (stopped?.Count > 0)
                    lines.AddRange(stopped);

                var recentIssues = snap.Timeline?
                    .Where(e => e.Timestamp >= DateTime.Now.AddHours(-1)
                             && (e.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                              || e.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (recentIssues?.Count > 0)
                    lines.Add($"Son 1 saatte {recentIssues.Count} hata/uyarı");

                if (snap.ListenPorts?.Count > 0)
                {
                    var portCount = snap.ListenPorts.Count;
                    if (portCount >= 8)
                        lines.Add($"{portCount} açık port");
                }

                if (!string.IsNullOrWhiteSpace(snap.LastLogon) && snap.LastLogon != "-")
                {
                    if (snap.LastLogon.Contains("gün", StringComparison.OrdinalIgnoreCase)
                        || snap.CollectedAt < DateTime.Now.AddHours(-6))
                        lines.Add("Son bağlantı eski");
                }
            }

            if (!string.IsNullOrWhiteSpace(healthReason) && lines.Count == 0)
                lines.Add(healthReason);

            if (!string.IsNullOrWhiteSpace(server.AlertMessage))
                lines.Add(server.AlertMessage);

            if (!string.IsNullOrWhiteSpace(server.LastEvent) && server.LastEvent != "Son olay yok" && server.LastEvent != "Olay yok")
                lines.Add(server.LastEvent);

            return lines.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        }

        private static List<string> BuildRecommendations(
            List<DailyServerFinding> critical,
            List<DailyServerFinding> warning,
            List<(ServerInfo Server, HealthBadge Badge, WmiSnapshot? Snap, long? PingMs, bool RdpOpen, string? Reason, bool HasLive)> all)
        {
            var recs = new List<string>();

            foreach (var f in critical.Take(3))
            {
                if (f.Summary.Contains("hata", StringComparison.OrdinalIgnoreCase)
                    || f.Summary.Contains("Event", StringComparison.OrdinalIgnoreCase)
                    || f.Details.Any(d => d.Contains("hata", StringComparison.OrdinalIgnoreCase)))
                {
                    recs.Add($"{f.ServerName} üzerindeki Event Log incelenmeli");
                }

                if (f.Details.Any(d => d.Contains("servis", StringComparison.OrdinalIgnoreCase)))
                    recs.Add($"{f.ServerName} servisleri kontrol edilmeli");
            }

            foreach (var f in warning.Where(w => w.Summary.Contains("Ping", StringComparison.OrdinalIgnoreCase)).Take(2))
                recs.Add($"{f.ServerName}: gecikme ve ağ yolu kontrol edilmeli");

            if (critical.Count > 0)
                recs.Add("Kritik sunucular için port/risk analizi yapılmalı");

            if (all.Any(x => x.Snap?.ListenPorts?.Count >= 8))
                recs.Add("Yüksek açık port sayısına sahip sunucular gözden geçirilmeli");

            if (recs.Count == 0 && (critical.Count > 0 || warning.Count > 0))
                recs.Add("Kritik ve uyarı durumundaki sunucuları dashboard'dan inceleyin");

            return recs.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        }
    }
}
