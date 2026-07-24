using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    internal static class ConsumerAppDetector
    {
        public static bool TryDetect(string? title, out string label)
        {
            label = string.Empty;
            if (string.IsNullOrWhiteSpace(title))
                return false;

            var haystack = title.ToLowerInvariant();
            foreach (var app in KnowledgePackHost.Current.ScreenCompatibility.ConsumerApps)
            {
                if (app.Patterns.Any(p => haystack.Contains(p, StringComparison.Ordinal)))
                {
                    label = app.Label;
                    return true;
                }
            }

            return false;
        }

        public static bool IsConsumerApp(MmcConsoleKind screenType, string displayName)
        {
            if (TryDetect(displayName, out _))
                return true;
            return screenType == MmcConsoleKind.Unknown
                   && !string.IsNullOrWhiteSpace(displayName)
                   && !displayName.Contains("Explorer", StringComparison.OrdinalIgnoreCase);
        }
    }
}
