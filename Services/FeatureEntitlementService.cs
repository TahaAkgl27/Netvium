using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    /// <summary>
    /// Central premium entitlement checks driven by the authenticated account's plan.
    /// </summary>
    public sealed class FeatureEntitlementService : IFeatureEntitlementService
    {
        public static FeatureEntitlementService Instance { get; } = new();
        private bool _isAccountAuthenticated;
        private string _accountPlanCode = "FREE";

        public bool IsDevelopmentMode => SettingsRuntime.Current.DevelopmentMode;

        /// <summary>
        /// Paid features are tied to an authenticated account. A guest always uses
        /// the Free plan; legacy local license files are not consulted.
        /// </summary>
        public bool IsAccountAuthenticated => _isAccountAuthenticated;

        public bool IsPremiumEnabled =>
            _isAccountAuthenticated &&
            _accountPlanCode is "PREMIUM" or "PREMIUM_PLUS";

        public bool IsAiGuideEnabled =>
            _isAccountAuthenticated && _accountPlanCode == "PREMIUM_PLUS";

        public void SetAccountAuthenticated(bool isAuthenticated, string? planCode = null)
        {
            _isAccountAuthenticated = isAuthenticated;
            _accountPlanCode = isAuthenticated
                ? (planCode ?? "FREE").Trim().ToUpperInvariant()
                : "FREE";
        }

        public bool HasFeature(PremiumFeature feature) => feature switch
        {
            PremiumFeature.AiAnalysis or PremiumFeature.RiskAnalysis =>
                IsPremiumEnabled,
            PremiumFeature.AiGuideVision or PremiumFeature.AiGuideSmartSummary or PremiumFeature.AiGuideWorkflow
                or PremiumFeature.AiGuideGuidance or PremiumFeature.AiGuideVisualMentor =>
                IsAiGuideEnabled,
            _ => false
        };

        public bool IsAiGuideVisionEnabled(AppSettings settings) =>
            settings.EnableAIGuideVision && IsAiGuideEnabled;

        public bool IsAiAnalysisEnabled(AppSettings settings) =>
            settings.EnableAiAnalysis && IsPremiumEnabled;
    }
}
