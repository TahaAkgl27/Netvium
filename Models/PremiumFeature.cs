namespace Netvium.Models
{
    /// <summary>Premium-gated product features. Licensing checks use this enum; restore gating by disabling DevelopmentMode.</summary>
    public enum PremiumFeature
    {
        AiAnalysis,
        RiskAnalysis,
        AiGuideVision,
        AiGuideSmartSummary,
        AiGuideWorkflow,
        AiGuideGuidance,
        AiGuideVisualMentor
    }
}
