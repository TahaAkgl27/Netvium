using Netvium.Models.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.KnowledgePack
{
    public static class KnowledgePackValidator
    {
        public static void Validate(KnowledgePackBundle bundle)
        {
            var errors = new List<string>();

            if (bundle.IntentPatterns.Rules.Count == 0)
                errors.Add("intent-patterns.tr.json: Rules must not be empty.");

            if (bundle.ScreenCompatibility.IntentScreens.Count == 0)
                errors.Add("screen-intent-compatibility.json: IntentScreens must not be empty.");

            if (bundle.WorkflowDefinitions.Workflows.Count == 0)
                errors.Add("workflow-definitions.json: Workflows must not be empty.");

            if (bundle.ResponseTemplates.Sections.Count == 0)
                errors.Add("response-templates.tr.json: Sections must not be empty.");

            if (!bundle.ConfidenceRules.Scoring.ContainsKey("screenCorrect"))
                errors.Add("confidence-rules.json: scoring.screenCorrect is required.");

            if (bundle.HallucinationRules.ActionKeywords.Count == 0)
                errors.Add("hallucination-rules.json: ActionKeywords must not be empty.");

            if (bundle.ContradictionRules.Rules.Count == 0)
                errors.Add("contradiction-rules.json: Rules must not be empty.");

            foreach (var wf in bundle.WorkflowDefinitions.Workflows)
            {
                if (string.IsNullOrWhiteSpace(wf.Id))
                    errors.Add("workflow-definitions.json: workflow id is required.");
                if (wf.Intents.Count == 0 && wf.Id != "generic")
                    errors.Add($"workflow-definitions.json: workflow '{wf.Id}' must declare at least one intent.");
            }

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "AI Guide KnowledgePack missing or invalid." + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }
}
