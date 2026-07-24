using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.KnowledgePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Detects logical contradictions using Knowledge Pack rules only.</summary>
    public sealed class ContradictionDetector
    {
        public ContradictionResult Detect(
            EvidenceBundle evidence,
            string instruction,
            WorkflowContext context,
            AssumptionAnalysis assumptions)
        {
            var rules = KnowledgePackHost.Current.ContradictionRules;
            var contradictions = new List<string>();
            var combined = instruction.ToLowerInvariant();
            var observation = context.ScreenObservation.ToLowerInvariant();

            var folder = ResolveFolderName(context);
            var saysNotVisible = rules.NotVisiblePhrases.Any(p =>
                observation.Contains(p, StringComparison.Ordinal)
                || evidence.Visual.Any(v => v.Statement.Contains(p, StringComparison.OrdinalIgnoreCase)));

            var hasAction = rules.ActionOnTargetPhrases.Any(p => combined.Contains(p, StringComparison.Ordinal));
            var hasNavigateSafe = rules.NavigateSafePhrases.Any(p => combined.Contains(p, StringComparison.Ordinal));
            var folderInInstruction = !string.IsNullOrWhiteSpace(folder)
                && combined.Contains(folder.ToLowerInvariant(), StringComparison.Ordinal);

            foreach (var rule in rules.Rules)
            {
                if (rule.RequiresTargetNotVisible && context.TargetVisible)
                    continue;
                if (rule.RequiresTargetNotVisible && !context.TargetVisible && !saysNotVisible && string.IsNullOrWhiteSpace(folder))
                    continue;
                if (rule.RequiresActionOnTarget && !hasAction)
                    continue;
                if (rule.RequiresFolderInInstruction && !folderInInstruction)
                    continue;
                if (rule.ForbidUnlessNavigatePhrase && hasNavigateSafe)
                    continue;
                if (rule.RequiresWrongScreen && !context.IsWrongScreen)
                    continue;
                if (rule.RequiresClarificationPending && !context.IsClarification)
                    continue;
                if (rule.RequiresWorkflowStepBeyondClarify
                    && (context.IsClarification || context.CurrentStep.StartsWith("clarify", StringComparison.OrdinalIgnoreCase)))
                    continue;

                contradictions.Add(rule.Description.Length > 0 ? rule.Description : rule.Id);
            }

            foreach (var unknown in assumptions.Unknown)
            {
                if (unknown.Contains("may not exist", StringComparison.OrdinalIgnoreCase)
                    && folderInInstruction
                    && hasAction
                    && !hasNavigateSafe)
                {
                    contradictions.Add($"Unknown folder existence conflicts with direct action on '{folder}'");
                    break;
                }
            }

            return new ContradictionResult
            {
                HasContradiction = contradictions.Count > 0,
                Contradictions = contradictions.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static string ResolveFolderName(WorkflowContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.Target.FolderName))
                return context.Target.FolderName;

            if (!string.IsNullOrWhiteSpace(context.Target.Path))
                return System.IO.Path.GetFileName(context.Target.Path.TrimEnd('\\'));

            return string.Empty;
        }
    }
}
