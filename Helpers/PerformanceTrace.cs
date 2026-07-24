using System;
using System.Diagnostics;

namespace Netvium.Helpers
{
    /// <summary>
    /// Lightweight Stopwatch logging for hot UI paths. Never logs secrets or raw IPs.
    /// </summary>
    public sealed class PerformanceTrace : IDisposable
    {
        private readonly string _operation;
        private readonly Stopwatch _sw;
        private bool _disposed;

        private PerformanceTrace(string operation)
        {
            _operation = operation;
            _sw = Stopwatch.StartNew();
        }

        public static PerformanceTrace Start(string operation) => new(operation);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sw.Stop();
            AppLogger.Debug($"[Perf] {_operation}={_sw.ElapsedMilliseconds}ms", category: "Perf", action: _operation);
        }
    }
}
