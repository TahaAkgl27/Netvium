using Netvium.Services;

namespace Netvium.Helpers
{
    internal static class AiGuideEntitlement
    {
        public static bool IsEnabled => FeatureEntitlementService.Instance.IsAiGuideEnabled;

        public static void EnsureEnabled()
        {
            if (!IsEnabled)
                throw new InvalidOperationException("AI Guide requires Premium+.");
        }
    }
}
