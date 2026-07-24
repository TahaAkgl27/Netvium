using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.KnowledgePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Single config-driven workflow guide loaded from workflow-definitions.json.</summary>
    public sealed class KnowledgePackWorkflowGuide : IWorkflowGuide
    {
        private readonly WorkflowDefinition _definition;

        public KnowledgePackWorkflowGuide(WorkflowDefinition definition) =>
            _definition = definition;

        public string WorkflowName => _definition.Id;

        public bool CanHandle(AdminUserIntent intent, MmcConsoleKind screenType)
        {
            if (_definition.Id == "generic")
                return true;
            return _definition.Intents.Any(i =>
                string.Equals(i, intent.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public WorkflowContext BuildContext(WorkflowGuideBuildInput input)
        {
            var targetVisible = WorkflowGuideSupport.IsTargetVisibleOnScreen(input.Target, input.VisualContext);
            var screenName = WorkflowGuideSupport.ResolveScreenName(
                input.VisualContext, input.ScreenType, input.ScreenDisplayName);

            var obsKey = WorkflowConditionEvaluator.PickObservationKey(_definition, input, targetVisible);
            var observation = _definition.Observations.TryGetValue(obsKey, out var obs)
                ? RenderObservation(obs, input, targetVisible)
                : string.Empty;

            var totalSteps = _definition.TotalStepsByScreen.TryGetValue(input.ScreenType.ToString(), out var ts)
                ? ts
                : _definition.DefaultTotalSteps;

            return new WorkflowContext
            {
                ScreenType = input.ScreenType,
                ScreenDisplayName = screenName,
                Intent = input.Intent,
                Target = input.Target,
                CurrentStepIndex = 1,
                TotalSteps = totalSteps,
                TargetVisible = targetVisible,
                WorkflowName = WorkflowName,
                UseTurkish = input.UseTurkish,
                ScreenObservation = observation,
                Evidence = BuildEvidence(input, targetVisible)
            };
        }

        public WorkflowStepResult GetNextStep(WorkflowContext context)
        {
            var buildInput = new WorkflowGuideBuildInput
            {
                UserQuestion = string.Empty,
                VisualContext = null,
                ScreenType = context.ScreenType,
                ScreenDisplayName = context.ScreenDisplayName,
                Intent = context.Intent,
                Target = context.Target,
                UseTurkish = context.UseTurkish
            };

            var specific = _definition.Steps.Where(s => !IsFallbackCondition(s.Condition));
            foreach (var step in specific)
            {
                if (!WorkflowConditionEvaluator.Matches(step.Condition, buildInput, context.TargetVisible))
                    continue;

                var instruction = RenderStep(step.Instruction, context);
                return WorkflowGuideSupport.Step(step.Id, 1, instruction);
            }

            foreach (var step in _definition.Steps.Where(s => IsFallbackCondition(s.Condition)))
            {
                if (!WorkflowConditionEvaluator.Matches(step.Condition, buildInput, context.TargetVisible))
                    continue;

                var instruction = RenderStep(step.Instruction, context);
                return WorkflowGuideSupport.Step(step.Id, 1, instruction);
            }

            var fallback = _definition.GenericStep.Get(context.UseTurkish);
            return WorkflowGuideSupport.Step("generic", 1, fallback);
        }

        private static bool IsFallbackCondition(string condition) =>
            string.IsNullOrWhiteSpace(condition) || condition == "always";

        private static string RenderObservation(LocalizedText template, WorkflowGuideBuildInput input, bool targetVisible)
        {
            var folder = !string.IsNullOrWhiteSpace(input.Target.FolderName)
                ? input.Target.FolderName
                : input.Target.Path;
            return template.Format(input.UseTurkish, new Dictionary<string, string>
            {
                ["folder"] = folder,
                ["path"] = input.Target.Path
            });
        }

        private static string RenderStep(LocalizedText template, WorkflowContext context)
        {
            var ipHint = !string.IsNullOrWhiteSpace(context.Target.IpAddress)
                ? $" ({context.Target.IpAddress})"
                : string.Empty;
            var folder = !string.IsNullOrWhiteSpace(context.Target.FolderName)
                ? context.Target.FolderName
                : context.Target.Path;

            return template.Format(context.UseTurkish, new Dictionary<string, string>
            {
                ["path"] = context.Target.Path,
                ["folder"] = folder,
                ["ipHint"] = ipHint
            });
        }

        private static IReadOnlyList<string> BuildEvidence(WorkflowGuideBuildInput input, bool targetVisible)
        {
            var list = new List<string> { input.ScreenDisplayName };
            if (input.Target.HasUserProvidedPath)
                list.Add($"Path: {input.Target.Path}");
            list.Add(targetVisible ? "Target visible" : "Target not visible");
            return list;
        }
    }
}
