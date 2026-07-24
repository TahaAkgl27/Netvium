using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    public sealed class HallucinationGuard
    {
        public HallucinationGuardResult Validate(
            WorkflowContext context,
            string instruction,
            IReadOnlyList<string> allInstructions,
            string? selectedListItem = null)
        {
            var rules = KnowledgePackHost.Current.HallucinationRules;
            var violations = new List<string>();
            var combined = string.Join(" ", allInstructions).ToLowerInvariant();
            var folder = context.Target.FolderName;

            if (!context.TargetVisible
                && !string.IsNullOrWhiteSpace(folder)
                && ContainsActionOnTarget(combined, folder, rules.ActionKeywords))
            {
                violations.Add($"Action on non-visible target folder '{folder}'");
            }

            if (!string.IsNullOrWhiteSpace(selectedListItem)
                && !string.IsNullOrWhiteSpace(folder)
                && !selectedListItem.Equals(folder, StringComparison.OrdinalIgnoreCase)
                && combined.Contains(selectedListItem.ToLowerInvariant(), StringComparison.Ordinal)
                && rules.ActionKeywords.Any(p => combined.Contains(p, StringComparison.Ordinal)))
            {
                violations.Add($"Action references wrong selection '{selectedListItem}' instead of '{folder}'");
            }

            if (!context.TargetVisible
                && context.Intent == AdminUserIntent.FolderPermission
                && rules.ActionKeywords.Any(p => combined.Contains(p, StringComparison.Ordinal))
                && !rules.NavigateKeywords.Any(p => combined.Contains(p, StringComparison.Ordinal)))
            {
                if (!violations.Any())
                    violations.Add("Permission action before target is visible");
            }

            if (violations.Count == 0)
            {
                return new HallucinationGuardResult
                {
                    Passed = true,
                    SanitizedInstruction = instruction
                };
            }

            var sanitized = BuildSafeInstruction(context, rules);
            return new HallucinationGuardResult
            {
                Passed = false,
                SanitizedInstruction = sanitized,
                Violations = violations
            };
        }

        private static bool ContainsActionOnTarget(string text, string folder, IReadOnlyList<string> actionKeywords)
        {
            if (!actionKeywords.Any(p => text.Contains(p, StringComparison.Ordinal)))
                return false;
            return text.Contains(folder.ToLowerInvariant(), StringComparison.Ordinal);
        }

        private static string BuildSafeInstruction(WorkflowContext context, Models.AIGuide.KnowledgePack.HallucinationRulesDocument rules)
        {
            if (context.Target.HasUserProvidedPath)
            {
                return rules.SafeNavigateWithPath.Format(context.UseTurkish, new Dictionary<string, string>
                {
                    ["path"] = context.Target.Path
                });
            }

            return rules.SafeNavigateGeneric.Get(context.UseTurkish);
        }
    }
}
