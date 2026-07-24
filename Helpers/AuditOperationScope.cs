using System;
using System.Diagnostics;
using Netvium.Helpers.Logging;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    /// <summary>
    /// Tracks a single auditable operation: logs Started, then Success/Failed/Cancelled with duration and correlation id.
    /// </summary>
    public sealed class AuditOperationScope : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly string _action;
        private readonly string _category;
        private readonly ServerInfo? _server;
        private readonly string? _sessionId;
        private readonly string _correlationId;
        private readonly string _level;
        private bool _completed;

        private AuditOperationScope(
            string action,
            string category,
            ServerInfo? server,
            string? sessionId,
            string level)
        {
            _action = action;
            _category = category;
            _server = server;
            _sessionId = sessionId;
            _correlationId = LoggingContext.CorrelationId;
            _level = level;

            Write(AuditResults.Started);
        }

        public static AuditOperationScope BeginRdp(
            string action,
            ServerInfo? server = null,
            string? sessionId = null,
            bool newCorrelation = false,
            string level = LogLevels.Information)
        {
            if (newCorrelation)
                LoggingContext.NewCorrelationId();

            return new AuditOperationScope(action, AuditCategories.Rdp, server, sessionId, level);
        }

        public static AuditOperationScope BeginRemoteAdmin(
            string action,
            ServerInfo? server = null,
            string? sessionId = null,
            bool newCorrelation = false)
        {
            if (newCorrelation)
                LoggingContext.NewCorrelationId();

            return new AuditOperationScope(action, AuditCategories.RemoteAdmin, server, sessionId, LogLevels.Security);
        }

        public static AuditOperationScope BeginAnalysis(
            string action,
            ServerInfo? server = null,
            bool newCorrelation = false)
        {
            if (newCorrelation)
                LoggingContext.NewCorrelationId();

            return new AuditOperationScope(action, AuditCategories.Analysis, server, null, LogLevels.Information);
        }

        public static AuditOperationScope BeginConfiguration(
            string action,
            bool newCorrelation = false)
        {
            if (newCorrelation)
                LoggingContext.NewCorrelationId();

            return new AuditOperationScope(action, AuditCategories.Configuration, null, null, LogLevels.Information);
        }

        public static AuditOperationScope BeginDataTransfer(
            string action,
            bool newCorrelation = false)
        {
            if (newCorrelation)
                LoggingContext.NewCorrelationId();

            return new AuditOperationScope(action, AuditCategories.Data, null, null, LogLevels.Security);
        }

        public void CompleteSuccess()
        {
            if (_completed) return;
            _completed = true;
            _stopwatch.Stop();
            Write(AuditResults.Success);
        }

        public void CompleteFailed(string? errorMessage, Exception? ex = null)
        {
            if (_completed) return;
            _completed = true;
            _stopwatch.Stop();

            if (ex != null)
                AppLogger.Error($"Audit operation failed: {_action}", ex, category: _category, action: _action);

            Write(AuditResults.Failed, errorMessage ?? ex?.Message);
        }

        public void CompleteCancelled(string? reason = null)
        {
            if (_completed) return;
            _completed = true;
            _stopwatch.Stop();
            Write(AuditResults.Cancelled, reason);
        }

        public void Dispose()
        {
            if (!_completed)
                CompleteCancelled("Operation abandoned without completion");
        }

        private void Write(string result, string? errorMessage = null)
        {
            AuditLogService.Log(
                _action,
                _category,
                result,
                _level,
                _server?.Name,
                _server?.Ip,
                _sessionId,
                _completed || result != AuditResults.Started ? _stopwatch.ElapsedMilliseconds : null,
                errorMessage,
                _correlationId);
        }
    }
}
