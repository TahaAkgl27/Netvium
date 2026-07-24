using System;
using System.IO;
using System.Linq;
using System.Text;
using Netvium.Helpers.Logging;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    public static class AppLogger
    {
        private static readonly object Gate = new();
        private static AppSettings _settings = AppSettings.CreateDefault();
        private static string _logDirectory = GetDefaultLogDirectory();
        private static string _currentLogFile = string.Empty;

        private const int MaxRetainedFiles = 30;

        public static void Configure(AppSettings settings)
        {
            _settings = settings;
            _logDirectory = GetApplicationLogDirectory();
            _currentLogFile = Path.Combine(_logDirectory, $"netvium_{DateTime.Now:yyyy-MM-dd}.log");
            Directory.CreateDirectory(_logDirectory);
            AuditLogService.Configure(settings.AuditLogging);
        }

        public static string GetApplicationLogDirectory()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Netvium", "Logs", "Application");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetDefaultLogDirectory() => GetApplicationLogDirectory();

        public static void Trace(string message, string? category = null, string? action = null) =>
            Write(LogLevels.Trace, message, null, category, action);

        public static void Debug(string message, string? category = null, string? action = null) =>
            Write(LogLevels.Debug, message, null, category, action);

        public static void Info(string message, string? category = null, string? action = null) =>
            Write(LogLevels.Information, message, null, category, action);

        public static void Warning(string message, Exception? ex = null, string? category = null, string? action = null) =>
            Write(LogLevels.Warning, message, ex, category, action);

        public static void Error(string message, Exception? ex = null, string? category = null, string? action = null) =>
            Write(LogLevels.Error, message, ex, category, action);

        public static void Critical(string message, Exception? ex = null, string? category = null, string? action = null) =>
            Write(LogLevels.Critical, message, ex, category, action);

        public static void Security(string message, string? category = null, string? action = null) =>
            Write(LogLevels.Security, message, null, category, action);

        public static void Audit(string message, string? category = null, string? action = null) =>
            Write(LogLevels.Audit, message, null, category, action);

        public static void Verbose(string message, string? category = null, string? action = null)
        {
            if (!_settings.VerboseLogs)
                return;
            Write(LogLevels.Debug, message, null, category, action);
        }

        public static IDisposable BeginScope(string category, string action, string? server = null, string? sessionId = null)
        {
            return new LogScope(category, action, server, sessionId);
        }

        private static void Write(
            string level,
            string message,
            Exception? ex,
            string? category,
            string? action,
            long? durationMs = null,
            string? server = null,
            string? sessionId = null)
        {
            if (!_settings.EnableLogging && level != LogLevels.Critical && level != LogLevels.Security)
                return;

            if (level == LogLevels.Trace && !_settings.VerboseLogs)
                return;

            if (level == LogLevels.Debug && !_settings.VerboseLogs)
                return;

            try
            {
                RotateIfNeeded();
                PruneOldFiles();

                var line = FormatLogLine(
                    level,
                    message,
                    ex,
                    category,
                    action,
                    durationMs,
                    server,
                    sessionId);

                lock (Gate)
                {
                    File.AppendAllText(GetCurrentLogFile(), line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception writeEx)
            {
                try
                {
                    var fallback = Path.Combine(Path.GetTempPath(), "netvium_logger_fallback.log");
                    File.AppendAllText(fallback,
                        $"{DateTime.Now:O} Logger failure: {SensitiveDataRedactor.Redact(writeEx.ToString())}{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch
                {
                    // Last resort — cannot log logger failure.
                }
            }
        }


        internal static string FormatForTesting(string message, Exception? exception) =>
            FormatLogLine(
                LogLevels.Error,
                message,
                exception,
                "Test",
                "Redaction",
                null,
                null,
                null);

        private static string FormatLogLine(
            string level,
            string message,
            Exception? exception,
            string? category,
            string? action,
            long? durationMs,
            string? server,
            string? sessionId)
        {
            string Safe(string? value, string fallback = "-") =>
                string.IsNullOrEmpty(value) ? fallback : SensitiveDataRedactor.Redact(value);

            var exceptionText = exception == null ? "-" : SensitiveDataRedactor.Redact(exception.ToString());
            var innerText = exception?.InnerException == null
                ? "-"
                : SensitiveDataRedactor.Redact(exception.InnerException.ToString());

            return string.Join("|",
                DateTime.Now.ToString("O"),
                Safe(level),
                Environment.CurrentManagedThreadId.ToString(),
                Safe(LoggingContext.CorrelationId),
                Safe(category),
                Safe(action),
                durationMs?.ToString() ?? "-",
                exceptionText.Replace(Environment.NewLine, " "),
                innerText.Replace(Environment.NewLine, " "),
                Safe(Environment.UserName),
                Safe(server),
                Safe(sessionId ?? LoggingContext.SessionId),
                Safe(message));
        }
        private static string GetCurrentLogFile()
        {
            if (string.IsNullOrEmpty(_currentLogFile))
                _currentLogFile = Path.Combine(_logDirectory, $"netvium_{DateTime.Now:yyyy-MM-dd}.log");
            return _currentLogFile;
        }

        private static void RotateIfNeeded()
        {
            var maxBytes = Math.Clamp(_settings.MaximumLogSizeMb, 1, 500) * 1024L * 1024L;

            var path = GetCurrentLogFile();
            if (!File.Exists(path))
                return;

            var info = new FileInfo(path);
            if (info.Length < maxBytes)
                return;

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var rotated = Path.Combine(_logDirectory, $"netvium_{stamp}.log");
            File.Move(path, rotated);
            _currentLogFile = Path.Combine(_logDirectory, $"netvium_{DateTime.Now:yyyy-MM-dd}.log");
        }

        private static void PruneOldFiles()
        {
            try
            {
                var files = Directory.GetFiles(_logDirectory, "netvium_*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in files.Skip(MaxRetainedFiles))
                {
                    try
                    {
                        if (_settings.SecureDeleteLogs)
                            CredentialProtector.SecureDeleteFile(file.FullName);
                        else
                            File.Delete(file.FullName);
                    }
                    catch (Exception ex)
                    {
                        // Log to current file on next write attempt; avoid recursion.
                        System.Diagnostics.Debug.WriteLine($"Log prune failed: {file.FullName} | {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log prune scan failed: {ex.Message}");
            }
        }

        public static string LogFilePath => GetCurrentLogFile();

        private sealed class LogScope : IDisposable
        {
            private readonly string _category;
            private readonly string _action;
            private readonly string? _server;
            private readonly string? _sessionId;
            private readonly DateTime _startedUtc;

            public LogScope(string category, string action, string? server, string? sessionId)
            {
                _category = category;
                _action = action;
                _server = server;
                _sessionId = sessionId;
                _startedUtc = DateTime.UtcNow;
            }

            public void Dispose()
            {
                var ms = (long)(DateTime.UtcNow - _startedUtc).TotalMilliseconds;
                Write(LogLevels.Information, "Completed", null, _category, _action, ms, _server, _sessionId);
            }
        }
    }
}
