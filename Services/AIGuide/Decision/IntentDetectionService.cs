using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.KnowledgePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Pattern-based intent detection from intent-patterns.tr.json.</summary>
    public sealed class IntentDetectionService
    {
        public AdminUserIntent Detect(string userQuestion, ScreenVisualContext? visualContext)
        {
            var pack = KnowledgePackHost.Current.IntentPatterns;
            var q = Normalize(userQuestion);
            if (string.IsNullOrWhiteSpace(q))
                return AdminUserIntent.Unknown;

            foreach (var rule in pack.Rules.OrderBy(r => r.Priority))
            {
                if (!string.IsNullOrWhiteSpace(rule.VisualContextField)
                    && !string.IsNullOrWhiteSpace(rule.VisualContextValue)
                    && MatchesVisualContext(visualContext, rule))
                {
                    if (TryParseIntent(rule.Intent, out var intent))
                        return intent;
                }

                if (rule.Keywords.Count > 0 && rule.Keywords.Any(k => q.Contains(k, StringComparison.Ordinal)))
                {
                    if (TryParseIntent(rule.Intent, out var intent))
                        return intent;
                }

                if (rule.AnyOf.Count > 0 && rule.AnyOf.Any(k => q.Contains(k, StringComparison.Ordinal)))
                {
                    if (TryParseIntent(rule.Intent, out var intent))
                        return intent;
                }
            }

            var folderIntent = FolderPermissionResolver.Resolve(userQuestion);
            if (folderIntent == AdminUserIntent.FolderPermission)
                return AdminUserIntent.FolderPermission;

            return AdminUserIntent.Unknown;
        }

        private static bool MatchesVisualContext(ScreenVisualContext? ctx, IntentPatternRule rule)
        {
            if (ctx == null) return false;
            return rule.VisualContextField switch
            {
                "DetectedEventId" => ctx.DetectedEventId == rule.VisualContextValue,
                _ => false
            };
        }

        private static bool TryParseIntent(string name, out AdminUserIntent intent) =>
            Enum.TryParse(name, ignoreCase: true, out intent);

        private static string Normalize(string text) =>
            (text ?? string.Empty).ToLowerInvariant();
    }
}
