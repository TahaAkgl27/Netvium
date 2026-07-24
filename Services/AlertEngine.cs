using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Netvium.Helpers.Logging;
using Netvium.Models;

namespace Netvium.Services
{
    public interface IAlertEngine
    {
        IReadOnlyList<AlertRule> Rules { get; }
        IReadOnlyList<AlertState> GetActiveAlerts();
        Task<AlertEvaluationResult> EvaluateAsync(ServerInfo server, WmiSnapshot? snapshot, long? latencyMs = null);
    }

    public sealed class AlertEngine : IAlertEngine
    {
        private readonly AlertStateStore _store;
        private readonly IAuditLogService _auditLog;
        private readonly object _gate = new();
        private List<AlertState> _activeAlerts;

        public AlertEngine(AlertStateStore? store = null, IAuditLogService? auditLog = null)
        {
            _store = store ?? new AlertStateStore();
            _auditLog = auditLog ?? AuditLogService.Instance;
            _activeAlerts = _store.LoadActive();
        }

        public IReadOnlyList<AlertState> GetActiveAlerts()
        {
            lock (_gate)
            {
                return _activeAlerts
                    .Where(a => string.Equals(a.Status, AlertStatuses.Active, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(a => a.LastSeen)
                    .ToList();
            }
        }

        public IReadOnlyList<AlertRule> Rules { get; } = new[]
        {
            new AlertRule { RuleId = "cpu-usage-high", Title = "CPU usage >= 90%", Severity = AlertSeverities.Critical, Threshold = "90%" },
            new AlertRule { RuleId = "ram-usage-high", Title = "RAM usage >= 90%", Severity = AlertSeverities.Critical, Threshold = "90%" },
            new AlertRule { RuleId = "disk-usage-high", Title = "Disk usage >= 90%", Severity = AlertSeverities.Critical, Threshold = "90%" },
            new AlertRule { RuleId = "firewall-disabled", Title = "Firewall disabled", Severity = AlertSeverities.Critical },
            new AlertRule { RuleId = "defender-disabled", Title = "Windows Defender disabled", Severity = AlertSeverities.Critical },
            new AlertRule { RuleId = "bitlocker-disabled", Title = "BitLocker disabled", Severity = AlertSeverities.Warning },
            new AlertRule { RuleId = "critical-service-stopped", Title = "Critical service stopped", Severity = AlertSeverities.Critical },
            new AlertRule { RuleId = "server-unreachable", Title = "Server unreachable", Severity = AlertSeverities.Critical },
            new AlertRule { RuleId = "risky-port-open", Title = "Open risky port detected", Severity = AlertSeverities.Warning },
            new AlertRule { RuleId = "windows-update-reboot-required", Title = "Windows Update reboot required", Severity = AlertSeverities.Warning }
        };

        public Task<AlertEvaluationResult> EvaluateAsync(ServerInfo server, WmiSnapshot? snapshot, long? latencyMs = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var triggered = EvaluateRules(server, snapshot, latencyMs, now);
                    var activeRuleIds = triggered.Select(a => a.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var created = new List<AlertState>();
                    var updated = new List<AlertState>();
                    var resolved = new List<AlertState>();

                    lock (_gate)
                    {
                        foreach (var candidate in triggered)
                        {
                            var existing = _activeAlerts.FirstOrDefault(a =>
                                string.Equals(a.ServerIp, candidate.ServerIp, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(a.RuleId, candidate.RuleId, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(a.Status, AlertStatuses.Active, StringComparison.OrdinalIgnoreCase));

                            if (existing == null)
                            {
                                created.Add(candidate);
                                _activeAlerts.Add(candidate);
                                _store.AppendHistory(candidate);
                                AuditCriticalAlertChange(AuditActions.AlertCreated, candidate);
                                continue;
                            }

                            existing.Timestamp = now;
                            existing.LastSeen = now;
                            existing.Severity = candidate.Severity;
                            existing.Title = candidate.Title;
                            existing.Message = candidate.Message;
                            existing.CurrentValue = candidate.CurrentValue;
                            existing.Threshold = candidate.Threshold;
                            existing.DetailsJson = candidate.DetailsJson;
                            updated.Add(existing);
                        }

                        foreach (var active in _activeAlerts
                                     .Where(a => string.Equals(a.ServerIp, server.Ip, StringComparison.OrdinalIgnoreCase)
                                                 && string.Equals(a.Status, AlertStatuses.Active, StringComparison.OrdinalIgnoreCase)
                                                 && !activeRuleIds.Contains(a.RuleId))
                                     .ToList())
                        {
                            active.Status = AlertStatuses.Resolved;
                            active.ResolvedAt = now;
                            active.LastSeen = now;
                            resolved.Add(active);
                            _store.AppendHistory(active);
                            AuditCriticalAlertChange(AuditActions.AlertResolved, active);
                        }

                        _activeAlerts = _activeAlerts
                            .Where(a => string.Equals(a.Status, AlertStatuses.Active, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        _store.SaveActive(_activeAlerts);
                    }

                    return new AlertEvaluationResult { Created = created, Updated = updated, Resolved = resolved };
                }
                catch
                {
                    return new AlertEvaluationResult();
                }
            });
        }

        private List<AlertState> EvaluateRules(ServerInfo server, WmiSnapshot? snap, long? latencyMs, DateTime now)
        {
            var alerts = new List<AlertState>();
            if (snap == null || !snap.Connected)
            {
                alerts.Add(CreateAlert(server, "server-unreachable", AlertSeverities.Critical,
                    "Server unreachable", snap?.Error ?? "No connected WMI/agent snapshot is available.", "Disconnected", "Connected", now,
                    new { latencyMs, snap?.Status, snap?.Error }));
                return alerts;
            }

            AddThresholdAlert(alerts, server, "cpu-usage-high", "CPU usage >= 90%", snap.CpuPct, 90, "%", now);
            AddThresholdAlert(alerts, server, "ram-usage-high", "RAM usage >= 90%", snap.RamUsedPct, 90, "%", now);
            AddThresholdAlert(alerts, server, "disk-usage-high", "Disk usage >= 90%", snap.DiskUsedPct, 90, "%", now);

            if (snap.FirewallDomainEnabled == false || snap.FirewallPrivateEnabled == false || snap.FirewallPublicEnabled == false
                || IsStopped(snap.FirewallServiceStatus))
            {
                alerts.Add(CreateAlert(server, "firewall-disabled", AlertSeverities.Critical,
                    "Firewall disabled", "One or more Windows Firewall profiles or the firewall service is disabled.",
                    FirewallState(snap), "All profiles enabled", now,
                    new { snap.FirewallDomainEnabled, snap.FirewallPrivateEnabled, snap.FirewallPublicEnabled, snap.FirewallServiceStatus }));
            }

            if (snap.DefenderRealTimeProtectionEnabled == false || IsDisabledText(snap.DefenderHealthState))
            {
                alerts.Add(CreateAlert(server, "defender-disabled", AlertSeverities.Critical,
                    "Windows Defender disabled", "Defender real-time protection or health state indicates disabled protection.",
                    snap.DefenderRealTimeProtectionEnabled == false ? "RealTimeProtection=False" : snap.DefenderHealthState, "Enabled", now,
                    new { snap.DefenderRealTimeProtectionEnabled, snap.DefenderHealthState, snap.DefenderThreatCount }));
            }

            var disabledBitLocker = snap.BitLockerVolumes
                .Where(v => IsDisabledText(v.ProtectionStatus) || v.ProtectionStatus.Contains("Off", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (disabledBitLocker.Count > 0)
            {
                alerts.Add(CreateAlert(server, "bitlocker-disabled", AlertSeverities.Warning,
                    "BitLocker disabled", $"{disabledBitLocker.Count} BitLocker volume(s) are not protected.",
                    string.Join(", ", disabledBitLocker.Select(v => $"{v.Volume}:{v.ProtectionStatus}")), "Protected", now,
                    disabledBitLocker));
            }

            var stoppedCritical = snap.Services
                .Where(s => s.IsCritical && !s.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (stoppedCritical.Count > 0)
            {
                alerts.Add(CreateAlert(server, "critical-service-stopped", AlertSeverities.Critical,
                    "Critical service stopped", $"{stoppedCritical.Count} critical service(s) are not running.",
                    string.Join(", ", stoppedCritical.Take(5).Select(s => $"{s.Display}:{s.State}")), "Running", now,
                    stoppedCritical.Take(20).ToList()));
            }

            var riskyPorts = snap.OpenPorts
                .Where(p => p.Risk.Contains("High", StringComparison.OrdinalIgnoreCase)
                            || p.Risk.Contains("Critical", StringComparison.OrdinalIgnoreCase)
                            || p.Risk.Contains("Risk", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (riskyPorts.Count == 0)
            {
                var riskyPortNumbers = new HashSet<int> { 135, 139, 445, 1433, 1521, 3306, 3389, 5985, 5986 };
                riskyPorts = snap.ListenPorts
                    .Where(riskyPortNumbers.Contains)
                    .Select(p => new OpenPortRow("TCP", p, 0, "Unknown", "Risk"))
                    .ToList();
            }
            if (riskyPorts.Count > 0)
            {
                alerts.Add(CreateAlert(server, "risky-port-open", AlertSeverities.Warning,
                    "Open risky port detected", $"{riskyPorts.Count} risky listening port(s) detected.",
                    string.Join(", ", riskyPorts.Take(8).Select(p => $"{p.Protocol}/{p.Port}:{p.Process}")), "No risky ports", now,
                    riskyPorts.Take(30).ToList()));
            }

            if (snap.WindowsUpdateRebootRequired == true)
            {
                alerts.Add(CreateAlert(server, "windows-update-reboot-required", AlertSeverities.Warning,
                    "Windows Update reboot required", "Windows Update reports that a reboot is required.",
                    "RebootRequired=True", "False", now,
                    new { snap.WindowsUpdatePendingCount, snap.WindowsUpdateLastInstall, snap.WindowsUpdateServiceStatus }));
            }

            return alerts;
        }

        private void AddThresholdAlert(List<AlertState> alerts, ServerInfo server, string ruleId, string title, double? value, double threshold, string unit, DateTime now)
        {
            if (!value.HasValue || value.Value < threshold)
                return;

            alerts.Add(CreateAlert(server, ruleId, AlertSeverities.Critical, title,
                $"{title} on {server.Name}.", $"{value.Value:0}{unit}", $"{threshold:0}{unit}", now,
                new { value, threshold }));
        }

        private AlertState CreateAlert(ServerInfo server, string ruleId, string severity, string title, string message, string? currentValue, string? threshold, DateTime now, object? details)
        {
            return new AlertState
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = now,
                ServerName = server.Name ?? server.Ip,
                ServerIp = server.Ip,
                Severity = severity,
                RuleId = ruleId,
                Title = title,
                Message = message,
                CurrentValue = currentValue,
                Threshold = threshold,
                Status = AlertStatuses.Active,
                FirstSeen = now,
                LastSeen = now,
                DetailsJson = details == null ? null : JsonSerializer.Serialize(details)
            };
        }

        private void AuditCriticalAlertChange(string action, AlertState alert)
        {
            if (!string.Equals(alert.Severity, AlertSeverities.Critical, StringComparison.OrdinalIgnoreCase))
                return;

            _auditLog.Write(new AuditLogEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                Severity = LogLevels.Warning,
                Category = AuditCategories.Alert,
                Action = action,
                Result = alert.Status,
                ServerName = alert.ServerName,
                ServerIP = alert.ServerIp,
                Username = Environment.UserName,
                MachineName = Environment.MachineName,
                Source = "AlertEngine",
                Message = $"{action}: {alert.Title} for {alert.ServerName} ({alert.ServerIp})",
                DetailsJson = JsonSerializer.Serialize(alert)
            });
        }

        private static bool IsStopped(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Contains("Stopped", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Kapalı", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Kapali", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Disabled", StringComparison.OrdinalIgnoreCase));

        private static bool IsDisabledText(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Off", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Pasif", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Kapalı", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Kapali", StringComparison.OrdinalIgnoreCase));

        private static string FirewallState(WmiSnapshot snap) =>
            $"Domain={snap.FirewallDomainEnabled?.ToString() ?? "Unknown"}, Private={snap.FirewallPrivateEnabled?.ToString() ?? "Unknown"}, Public={snap.FirewallPublicEnabled?.ToString() ?? "Unknown"}, Service={snap.FirewallServiceStatus ?? "Unknown"}";
    }
}
