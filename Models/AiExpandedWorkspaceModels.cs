using System.Collections.Generic;

namespace Netvium.Models
{
    public enum AiInfraStatus { Healthy, Warning, Critical, Unknown }
    public enum AiDeltaDirection { Up, Down, Stable, Unknown }

    public sealed class AiExpandedRiskItem
    {
        public string Title { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string BusinessImpact { get; init; } = string.Empty;
        public string TechnicalImpact { get; init; } = string.Empty;
        public string Confidence { get; init; } = string.Empty;
        public string SeverityColor { get; init; } = "#94A3B8";
    }

    public sealed class AiActionCenterCard
    {
        public string Priority { get; init; } = string.Empty;
        public string PriorityColor { get; init; } = "#F59E0B";
        public string Title { get; init; } = string.Empty;
        public string ExpectedImprovement { get; init; } = string.Empty;
        public string EstimatedDuration { get; init; } = string.Empty;
        public string RequiredPermissions { get; init; } = string.Empty;
        public string AutomationNote { get; init; } = string.Empty;
        public string CommandExample { get; init; } = string.Empty;
        public int RiskBefore { get; init; }
        public int RiskAfter { get; init; }
        public string BusinessImpactReduction { get; init; } = string.Empty;
    }

    public sealed class AiInfraHealthNode
    {
        public string Name { get; init; } = string.Empty;
        public AiInfraStatus Status { get; init; }
        public string StatusLabel { get; init; } = "Unknown";
        public string StatusColor { get; init; } = "#64748B";
        public string IconGlyph { get; init; } = "\uE774";
    }

    public sealed class AiRiskDistributionVisual
    {
        public int Critical { get; init; }
        public int High { get; init; }
        public int Medium { get; init; }
        public int Low { get; init; }
        public int Total => Critical + High + Medium + Low;
    }

    public sealed class AiChangeIndicator
    {
        public string Metric { get; init; } = string.Empty;
        public string CurrentValue { get; init; } = string.Empty;
        public AiDeltaDirection Direction { get; init; }
        public string ArrowGlyph { get; init; } = "\uE910";
        public string DeltaLabel { get; init; } = string.Empty;
        public string AccentColor { get; init; } = "#94A3B8";
    }

    public sealed class AiTimelineNode
    {
        public string TimeLabel { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string SeverityColor { get; init; } = "#4F8CFF";
        public bool ShowConnector { get; init; } = true;
    }

    public sealed class AiReasoningStepVisual
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string IconGlyph { get; init; } = "\uE774";
        public bool IsComplete { get; init; }
    }

    public sealed class AiBusinessImpactVisual
    {
        public string Area { get; init; } = string.Empty;
        public string ImpactLevel { get; init; } = "No Impact";
        public string ImpactColor { get; init; } = "#22C55E";
        public double ImpactFill { get; init; }
    }

    public sealed class AiPredictionVisual
    {
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string SeverityColor { get; init; } = "#64748B";
        public bool Available { get; init; }
    }

    public sealed class AiLiveInsightCard
    {
        public string IconGlyph { get; init; } = "\uE9F5";
        public string Title { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public string StatusColor { get; init; } = "#4F8CFF";
        public double ActivityLevel { get; init; }
    }

    public sealed class AiSparklineVisual
    {
        public string Label { get; init; } = string.Empty;
        public string CurrentValue { get; init; } = string.Empty;
        public string TrendLabel { get; init; } = string.Empty;
        public IReadOnlyList<double> Points { get; init; } = new List<double>();
        public string AccentColor { get; init; } = "#4F8CFF";
    }

    /// <summary>V2 display state for expanded AI Operations Center (read-only).</summary>
    public sealed class AiExpandedWorkspaceState
    {
        // Section 1 — Executive Dashboard
        public int HealthScoreValue { get; init; }
        public string HealthScoreDisplay { get; init; } = "—";
        public string RiskLevel { get; init; } = "—";
        public string RiskLevelColor { get; init; } = "#F59E0B";
        public int AiConfidenceValue { get; init; }
        public string AiConfidenceDisplay { get; init; } = "—";
        public string BusinessImpactLevel { get; init; } = "Unknown";
        public string BusinessImpactColor { get; init; } = "#64748B";
        public string InfrastructureStatus { get; init; } = "Unknown";
        public string InfrastructureStatusColor { get; init; } = "#64748B";
        public string EstimatedFixTime { get; init; } = "—";
        public string AnalysisAge { get; init; } = "—";
        public string AnalysisSource { get; init; } = "—";
        public string ServerRole { get; init; } = "—";
        public string Headline { get; init; } = "Awaiting analysis";
        public string Subheadline { get; init; } = "Run refresh after live telemetry is available.";

        // Section 2
        public IReadOnlyList<AiInfraHealthNode> InfrastructureMap { get; init; } = new List<AiInfraHealthNode>();

        // Section 3
        public AiRiskDistributionVisual RiskDistribution { get; init; } = new();
        public IReadOnlyList<AiExpandedRiskItem> CriticalRisks { get; init; } = new List<AiExpandedRiskItem>();
        public IReadOnlyList<AiExpandedRiskItem> HighRisks { get; init; } = new List<AiExpandedRiskItem>();
        public IReadOnlyList<AiExpandedRiskItem> MediumRisks { get; init; } = new List<AiExpandedRiskItem>();
        public IReadOnlyList<AiExpandedRiskItem> LowRisks { get; init; } = new List<AiExpandedRiskItem>();

        // Section 4
        public IReadOnlyList<AiChangeIndicator> Changes { get; init; } = new List<AiChangeIndicator>();

        // Section 5
        public IReadOnlyList<AiTimelineNode> Timeline { get; init; } = new List<AiTimelineNode>();

        // Section 6
        public IReadOnlyList<AiReasoningStepVisual> ReasoningPipeline { get; init; } = new List<AiReasoningStepVisual>();

        // Section 7
        public IReadOnlyList<AiBusinessImpactVisual> BusinessImpacts { get; init; } = new List<AiBusinessImpactVisual>();

        // Section 8
        public IReadOnlyList<AiActionCenterCard> ActionCenter { get; init; } = new List<AiActionCenterCard>();

        // Section 9
        public IReadOnlyList<AiPredictionVisual> Predictions { get; init; } = new List<AiPredictionVisual>();

        // Section 10
        public IReadOnlyList<AiLiveInsightCard> LiveInsights { get; init; } = new List<AiLiveInsightCard>();

        // Section 11
        public IReadOnlyList<AiSparklineVisual> TrendSparklines { get; init; } = new List<AiSparklineVisual>();
    }
}
