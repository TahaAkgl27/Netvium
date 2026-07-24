using System;
using System.Collections.Generic;

namespace Netvium.Models
{
    public sealed class DailyServerFinding
    {
        public string ServerName { get; init; } = string.Empty;
        public string Ip { get; init; } = string.Empty;
        public HealthBadge Status { get; init; }
        public string Summary { get; init; } = string.Empty;
        public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
        public DateTime? LastSeen { get; init; }
        public string? Latency { get; init; }
        public bool HasLiveData { get; init; }
    }

    public sealed class DailyAnalysisReport
    {
        public int TotalServers { get; init; }
        public int HealthyCount { get; init; }
        public int WarningCount { get; init; }
        public int CriticalCount { get; init; }
        public int OfflineCount { get; init; }
        public bool HasAnyLiveData { get; init; }
        public bool IsFailed { get; init; }
        public string? FailureMessage { get; init; }
        public IReadOnlyList<DailyServerFinding> CriticalFindings { get; init; } = Array.Empty<DailyServerFinding>();
        public IReadOnlyList<DailyServerFinding> WarningFindings { get; init; } = Array.Empty<DailyServerFinding>();
        public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

        public static DailyAnalysisReport NoServers() => new()
        {
            IsFailed = true,
            FailureMessage = "Kayıtlı sunucu bulunamadı. Önce sunucu ekleyin."
        };

        public static DailyAnalysisReport NoLiveData(int total) => new()
        {
            TotalServers = total,
            IsFailed = true,
            FailureMessage = "Analiz yapılamadı, canlı veri bulunamadı."
        };
    }
}
