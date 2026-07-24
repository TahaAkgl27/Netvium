using System;
using System.Collections.Generic;

namespace Netvium.Models
{
    public static class AlertSeverities
    {
        public const string Info = "Info";
        public const string Warning = "Warning";
        public const string Critical = "Critical";
    }

    public static class AlertStatuses
    {
        public const string Active = "Active";
        public const string Resolved = "Resolved";
    }

    public sealed class AlertRule
    {
        public string RuleId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Severity { get; init; } = AlertSeverities.Warning;
        public string? Threshold { get; init; }
    }

    public sealed class AlertState
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ServerName { get; set; } = string.Empty;
        public string ServerIp { get; set; } = string.Empty;
        public string Severity { get; set; } = AlertSeverities.Warning;
        public string RuleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? CurrentValue { get; set; }
        public string? Threshold { get; set; }
        public string Status { get; set; } = AlertStatuses.Active;
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public bool Acknowledged { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? DetailsJson { get; set; }
    }

    public sealed class AlertEvaluationResult
    {
        public IReadOnlyList<AlertState> Created { get; init; } = Array.Empty<AlertState>();
        public IReadOnlyList<AlertState> Updated { get; init; } = Array.Empty<AlertState>();
        public IReadOnlyList<AlertState> Resolved { get; init; } = Array.Empty<AlertState>();
    }
}
