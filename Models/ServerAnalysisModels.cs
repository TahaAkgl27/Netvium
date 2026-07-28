using System;
using System.Collections.Generic;

namespace Netvium.Models
{
    public sealed class AnalysisFinding
    {
        public string Code { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Evidence { get; init; } = string.Empty;
        public InsightSeverity Severity { get; init; }
        public string Category { get; init; } = "General";
    }

    public enum MetricTrendState
    {
        Unknown, Normal, InsufficientHistory, CriticalSpike, SustainedWarning, SustainedCritical
    }

    public sealed class MetricTrendAssessment
    {
        public string Metric { get; init; } = string.Empty;
        public MetricTrendState State { get; init; }
        public double? CurrentValue { get; init; }
        public int AvailableSamples { get; init; }
        public int SamplesAboveWarning { get; init; }
        public int SamplesAboveCritical { get; init; }
        public TimeSpan CoveredWindow { get; init; }
        public string Evidence { get; init; } = string.Empty;
    }

    public sealed class EventSeveritySummary
    {
        public int CriticalCount { get; init; }
        public int ErrorCount { get; init; }
        public int WarningCount { get; init; }
        public int InfoCount { get; init; }
        public int TotalCount => CriticalCount + ErrorCount + WarningCount + InfoCount;
    }

    public sealed class SystemHealthAnalysisData
    {
        public bool? DefenderEnabled { get; init; }
        public bool? ThirdPartyAntivirusHealthy { get; init; }
        public string? ThirdPartyAntivirusName { get; init; }
        public bool? FirewallDomainEnabled { get; init; }
        public bool? FirewallPrivateEnabled { get; init; }
        public bool? FirewallPublicEnabled { get; init; }
        public bool? PendingRestart { get; init; }
        public string? PendingRestartSignal { get; init; }
        public string? DnsStatus { get; init; }
        public string? TimeSyncStatus { get; init; }
    }

    public sealed class AnalysisDataQuality
    {
        public string Source { get; init; } = SnapshotDataSources.Wmi;
        public DateTime? CollectedAtUtc { get; init; }
        public TimeSpan? Age { get; init; }
        public bool IsCache { get; init; }
        public bool IsStale { get; init; }
        public int ConfidencePercent { get; set; } = 100;
        public bool SourcesCompared { get; init; }
        public bool HasSourceConflict { get; init; }
        public List<string> UnavailableComponents { get; init; } = new();
        public List<string> Conflicts { get; init; } = new();
    }
    public sealed class ServerAnalysisContext
    {
        public string ServerKey { get; init; } = string.Empty;
        public string? OsName { get; init; }
        public int? CpuPercent { get; init; }
        public double? RamPercent { get; init; }
        public double? DiskPercent { get; init; }
        public long? LatencyMs { get; init; }
        public string? NetworkSummary { get; init; }
        public string? UptimeText { get; init; }
        public bool RdpOpen { get; init; }
        public bool WinRmRunning { get; init; }
        public bool FirewallRunning { get; init; }
        public bool AntivirusRunning { get; init; }
        public string? RemoteRegistryState { get; init; }
        public bool SmbRunning { get; init; }
        public string? LastUser { get; init; }
        public int HealthScore { get; set; }
        public int RiskScore { get; set; }
        public string DetectedRole { get; init; } = "Application Server";
        public int RoleConfidence { get; init; }
        public List<string> RoleEvidence { get; init; } = new();
        public List<string> InstalledRoles { get; init; } = new();
        public List<string> RunningServices { get; init; } = new();
        public List<string> StoppedServices { get; init; } = new();
        public List<string> StoppedCriticalServices { get; init; } = new();
        public List<string> OpenPorts { get; init; } = new();
        public List<string> RecentEventLogs { get; init; } = new();
        public EventSeveritySummary EventSummary { get; init; } = new();
        public SystemHealthAnalysisData SystemHealth { get; init; } = new();
        public AnalysisDataQuality DataQuality { get; init; } = new();
        public MetricTrendAssessment CpuTrendAssessment { get; init; } = new();
        public MetricTrendAssessment RamTrendAssessment { get; init; } = new();
        public MetricTrendAssessment DiskTrendAssessment { get; init; } = new();
        public List<AnalysisFinding> RuleFindings { get; set; } = new();
        public List<AnalysisFinding> TopFindings { get; set; } = new();
        public bool HasWmiData { get; init; }
        public AiPredictionSection? DiskTrendPrediction { get; set; }
        public SnapshotTrendData? Trends { get; set; }
    }

    public sealed class AiSmartAnalysisReport
    {
        public AiServerRoleSection? ServerRole { get; set; }
        public AiFindingItem? HealthScore { get; set; }
        public List<AiFindingItem> CriticalFindings { get; set; } = new();
        public List<AiFindingItem> Performance { get; set; } = new();
        public List<AiFindingItem> Security { get; set; } = new();
        public List<AiFindingItem> EventAnalysis { get; set; } = new();
        public AiPredictionSection? Prediction { get; set; }
        public List<AiActionItem> RecommendedActions { get; set; } = new();
        public List<AiEvidenceItem> Evidence { get; set; } = new();
        public bool FromAi { get; set; }
    }

    public sealed class AiServerRoleSection
    {
        public string Role { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public List<string> Evidence { get; set; } = new();
    }

    public sealed class AiFindingItem
    {
        public string Text { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
        public string? RiskLevel { get; set; }
    }

    public sealed class AiPredictionSection
    {
        public string Text { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
        public bool Available { get; set; }
    }

    public sealed class AiActionItem
    {
        public string Priority { get; set; } = "MEDIUM";
        public string Text { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
    }

    public sealed class AiEvidenceItem
    {
        public string Metric { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
