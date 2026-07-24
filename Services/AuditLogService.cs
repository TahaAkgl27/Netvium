using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Netvium.Helpers.Logging;
using Netvium.Models;
using Netvium.Helpers;

namespace Netvium.Services
{
    public interface IAuditLogService
    {
        void Write(AuditLogEntry entry);
        System.Threading.Tasks.Task WriteAsync(AuditLogEntry entry);
    }

    public sealed class AuditLogService : IAuditLogService
    {
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static AuditLogService Instance { get; } = new();

        private static string? _appVersion;
        private static bool _auditEnabled = true;

        private AuditLogService()
        {
        }

        public static void Configure(bool auditLoggingEnabled)
        {
            _auditEnabled = auditLoggingEnabled;
            EnsureDirectory();
        }

        public static string GetAuditDirectory()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvium", "audit");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        private static void EnsureDirectory() => _ = GetAuditDirectory();

        public static void Log(
            string action,
            string category,
            string result,
            string level = LogLevels.Information,
            string? serverName = null,
            string? serverIp = null,
            string? sessionId = null,
            long? durationMs = null,
            string? errorMessage = null,
            string? correlationId = null,
            string? message = null,
            string? detailsJson = null)
        {
            if (!_auditEnabled)
                return;

            try
            {
                var entry = new AuditLogEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    Severity = level,
                    Username = GetCurrentUsername(),
                    MachineName = Environment.MachineName,
                    Source = "Netvium",
                    ApplicationVersion = GetApplicationVersion(),
                    ServerName = serverName,
                    ServerIP = serverIp,
                    SessionId = sessionId ?? LoggingContext.SessionId,
                    Action = action,
                    Category = category,
                    Result = result,
                    DurationMs = durationMs,
                    ErrorMessage = errorMessage,
                    CorrelationId = correlationId ?? LoggingContext.CorrelationId,
                    Level = level,
                    Message = message ?? BuildMessage(category, action, result, serverName, serverIp, errorMessage),
                    DetailsJson = detailsJson
                };

                Instance.Write(entry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audit] Failed to queue audit log entry: {ex.Message}");
            }
        }

        public static void LogRequest(
            string action,
            string category,
            ServerInfo? server = null,
            string? message = null,
            string? detailsJson = null,
            string level = LogLevels.Security)
        {
            Log(action, category, AuditResults.Started, level, server?.Name, server?.Ip,
                message: message, detailsJson: detailsJson);
        }

        public static void LogSecurity(
            string action,
            string category,
            string result,
            string? serverName = null,
            string? serverIp = null,
            string? errorMessage = null)
        {
            Log(action, category, result, LogLevels.Security, serverName, serverIp,
                errorMessage: errorMessage);
        }

        public static void PurgeExpiredLogs(int retentionDays = 90)
        {
            try
            {
                var dir = GetAuditDirectory();
                var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
                foreach (var file in Directory.GetFiles(dir, "audit-*.jsonl"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Audit] Failed to purge {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audit] Purge failed: {ex.Message}");
            }
        }

        public void Write(AuditLogEntry entry)
        {
            _ = WriteAsync(entry);
        }

        public System.Threading.Tasks.Task WriteAsync(AuditLogEntry entry)
        {
            if (!_auditEnabled)
                return System.Threading.Tasks.Task.CompletedTask;

            entry = RedactEntry(entry);

            return System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var line = JsonSerializer.Serialize(entry, JsonOpts) + Environment.NewLine;
                    var file = Path.Combine(GetAuditDirectory(), $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
                    lock (Gate)
                    {
                        File.AppendAllText(file, line, Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Audit] Failed to write audit log entry: {SensitiveDataRedactor.Redact(ex.Message)}");
                }
            });
        }


        internal static AuditLogEntry RedactForTesting(AuditLogEntry entry) => RedactEntry(entry);

        private static AuditLogEntry RedactEntry(AuditLogEntry entry)
        {
            string Safe(string? value) => SensitiveDataRedactor.Redact(value);

            return new AuditLogEntry
            {
                Id = Safe(entry.Id),
                Timestamp = entry.Timestamp,
                Severity = Safe(entry.Severity),
                Username = Safe(entry.Username),
                MachineName = Safe(entry.MachineName),
                Source = Safe(entry.Source),
                Message = Safe(entry.Message),
                DetailsJson = entry.DetailsJson == null ? null : Safe(entry.DetailsJson),
                ApplicationVersion = Safe(entry.ApplicationVersion),
                ServerName = entry.ServerName == null ? null : Safe(entry.ServerName),
                ServerIP = entry.ServerIP == null ? null : Safe(entry.ServerIP),
                SessionId = entry.SessionId == null ? null : Safe(entry.SessionId),
                Action = Safe(entry.Action),
                Category = Safe(entry.Category),
                Result = Safe(entry.Result),
                DurationMs = entry.DurationMs,
                ErrorMessage = entry.ErrorMessage == null ? null : Safe(entry.ErrorMessage),
                CorrelationId = Safe(entry.CorrelationId),
                Level = Safe(entry.Level)
            };
        }
        private static string GetCurrentUsername()
        {
            try
            {
                return WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not resolve Windows identity: {ex.Message}");
                return Environment.UserName;
            }
        }

        private static string GetApplicationVersion()
        {
            if (_appVersion != null)
                return _appVersion;

            _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            return _appVersion;
        }

        private static string BuildMessage(string category, string action, string result, string? serverName, string? serverIp, string? error)
        {
            var target = !string.IsNullOrWhiteSpace(serverName) || !string.IsNullOrWhiteSpace(serverIp)
                ? $" for {serverName ?? serverIp} ({serverIp ?? "-"})"
                : string.Empty;
            var suffix = string.IsNullOrWhiteSpace(error) ? string.Empty : $": {error}";
            return $"{category}.{action} {result}{target}{suffix}";
        }
    }
}
