using Netvium.Models;

namespace Netvium.Services
{
    /// <summary>Entitlement surface for premium features. Preserved for future subscription licensing.</summary>
    public interface IFeatureEntitlementService
    {
        bool IsDevelopmentMode { get; }
        bool IsPremiumEnabled { get; }
        bool IsAiGuideEnabled { get; }
        bool HasFeature(PremiumFeature feature);
        bool IsAiGuideVisionEnabled(AppSettings settings);
        bool IsAiAnalysisEnabled(AppSettings settings);
    }
}
