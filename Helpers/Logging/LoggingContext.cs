using System;
using System.Threading;

namespace Netvium.Helpers.Logging
{
    /// <summary>Ambient correlation and session identifiers for structured logging.</summary>
    public static class LoggingContext
    {
        private static readonly AsyncLocal<string?> Correlation = new();
        private static readonly AsyncLocal<string?> Session = new();

        public static string CorrelationId
        {
            get => Correlation.Value ??= Guid.NewGuid().ToString("N");
            set => Correlation.Value = value;
        }

        public static string? SessionId
        {
            get => Session.Value;
            set => Session.Value = value;
        }

        public static string NewCorrelationId()
        {
            Correlation.Value = Guid.NewGuid().ToString("N");
            return Correlation.Value;
        }

        public static void ClearSession() => Session.Value = null;
    }
}
