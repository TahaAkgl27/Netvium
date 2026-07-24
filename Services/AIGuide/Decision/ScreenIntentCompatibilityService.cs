using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    public sealed class ScreenIntentCompatibilityService
    {
        public ScreenIntentCompatibilityResult Evaluate(
            AdminUserIntent intent,
            MmcConsoleKind screenType,
            string displayName)
        {
            var pack = KnowledgePackHost.Current.ScreenCompatibility;
            var rule = pack.IntentScreens.FirstOrDefault(r =>
                string.Equals(r.Intent, intent.ToString(), StringComparison.OrdinalIgnoreCase));

            if (rule == null)
            {
                return new ScreenIntentCompatibilityResult
                {
                    IsCompatible = true,
                    AllowedScreenLabels = ["Any"]
                };
            }

            var isConsumer = ConsumerAppDetector.IsConsumerApp(screenType, displayName);
            var allowed = rule.AllowedScreens
                .Select(ParseScreenKind)
                .Where(k => k != MmcConsoleKind.Unknown)
                .ToHashSet();

            var compatible = allowed.Contains(screenType) && !isConsumer;

            if (isConsumer && intent != AdminUserIntent.Unknown)
                compatible = false;

            if (screenType == MmcConsoleKind.Unknown && !isConsumer && intent != AdminUserIntent.FolderPermission)
                compatible = false;

            if (intent == AdminUserIntent.FolderPermission && screenType == MmcConsoleKind.Unknown && !isConsumer)
                compatible = false;

            return new ScreenIntentCompatibilityResult
            {
                IsCompatible = compatible,
                IsConsumerApp = isConsumer,
                AllowedScreenLabels = rule.AllowedScreenLabels.Count > 0
                    ? rule.AllowedScreenLabels
                    : rule.AllowedScreens,
                Reason = compatible
                    ? string.Empty
                    : isConsumer
                        ? $"{ResolveDisplayLabel(screenType, displayName)} is not an admin console for {intent}."
                        : $"Screen {ResolveDisplayLabel(screenType, displayName)} cannot perform {intent}."
            };
        }

        public static string ResolveDisplayLabel(MmcConsoleKind screenType, string displayName)
        {
            if (ConsumerAppDetector.TryDetect(displayName, out var app))
                return app;
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
            return MmcConsoleDetector.SnapInDisplayName(screenType) is { Length: > 0 } n ? n : "Unknown";
        }

        private static MmcConsoleKind ParseScreenKind(string name) =>
            Enum.TryParse<MmcConsoleKind>(name, ignoreCase: true, out var kind) ? kind : MmcConsoleKind.Unknown;
    }
}
