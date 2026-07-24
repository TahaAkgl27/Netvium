using System;

namespace Netvium.Models
{
    public sealed class ConnectionHistoryEntry
    {
        public string ServerName { get; init; } = string.Empty;
        public string ServerIp { get; init; } = string.Empty;
        public DateTime Time { get; init; }
        public long? DurationMs { get; init; }
        public bool Success { get; init; }
        public bool Failed { get; init; }
        public string? DisconnectReason { get; init; }
        public string Action { get; init; } = string.Empty;
        public string? SessionId { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
    }
}
