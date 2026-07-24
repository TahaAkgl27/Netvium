using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    internal static class ClarificationResultBuilder
    {
        public static WorkflowDecisionResult Build(
            AdminUserIntent intent,
            MmcConsoleKind screenType,
            string displayName,
            WorkflowTarget target,
            ClarificationResult clarification,
            bool useTurkish,
            IReadOnlyList<string> visibleObjects,
            ConfidenceEngine confidenceEngine)
        {
            var confidence = confidenceEngine.Evaluate(new ConfidenceInput
            {
                ClarificationRequired = true,
                IntentDetected = intent != AdminUserIntent.Unknown,
                ScreenCorrect = screenType != MmcConsoleKind.Unknown,
                MissingRequiredTarget = clarification.MissingFields.Count >= 2
            });

            var context = new WorkflowContext
            {
                ScreenType = screenType,
                ScreenDisplayName = displayName,
                Intent = intent,
                Target = target,
                WorkflowName = "clarification",
                UseTurkish = useTurkish,
                IsClarification = true,
                ShouldAskQuestion = true,
                ShouldContinue = false,
                MissingFields = clarification.MissingFields,
                ClarificationQuestion = clarification.Question,
                ConfidenceCandidate = clarification.ConfidenceLabel.Length > 0
                    ? clarification.ConfidenceLabel
                    : confidence.Label,
                ConfidenceScore = confidence.Score,
                VisibleObjects = visibleObjects,
                ScreenObservation = TemplateRenderer.Section("clarificationNeeded", useTurkish)
            };

            return new WorkflowDecisionResult
            {
                HasWorkflow = true,
                UseLocalResponse = true,
                ShouldAskQuestion = true,
                ShouldContinue = false,
                Context = context,
                NextStep = WorkflowGuideSupport.Step("clarify", 1, clarification.Question, false),
                ManualStepInstructions = [clarification.Question],
                StructuredSummary = $"Clarification; Intent={intent}; Missing={clarification.MissingFields.Count}"
            };
        }
    }
}
