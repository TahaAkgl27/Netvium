using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Maps WorkflowDecisionResult to legacy hints for logging and backward-compatible consumers.</summary>
    internal static class LegacyGuidanceMapper
    {
        public static AdminToolGuidanceHint ToToolHint(WorkflowDecisionResult decision, ScreenVisualContext? visualContext, string userQuestion)
        {
            var legacy = AdminToolExpertEngine.Analyze(visualContext, userQuestion);
            return new AdminToolGuidanceHint
            {
                VisibleTool = decision.Context.ScreenType,
                VisibleToolName = decision.Context.ScreenDisplayName,
                UserIntent = decision.Context.Intent,
                RecommendedTool = legacy.RecommendedTool,
                RecommendedToolName = legacy.RecommendedToolName,
                IsToolMismatch = legacy.IsToolMismatch,
                IsCorrectTool = legacy.IsCorrectTool,
                ExpertModuleId = legacy.ExpertModuleId,
                ExpertKnowledge = legacy.ExpertKnowledge,
                HasModalPriority = legacy.HasModalPriority,
                ModalTitle = legacy.ModalTitle,
                ModalSummary = legacy.ModalSummary,
                FocusedContext = legacy.FocusedContext,
                PromptDirective = $"Workflow={decision.Context.WorkflowName}; Step={decision.NextStep.StepId}"
            };
        }

        public static NavigationGuidanceHint ToNavigationHint(
            WorkflowDecisionResult decision,
            bool includeFormattedAnswer = false,
            string requestId = "decision")
        {
            string? localAnswer = null;
            if (includeFormattedAnswer && decision.UseLocalResponse)
                localAnswer = new WorkflowResponseGenerator().Generate(decision, requestId, suppressManualControlNote: true).FormattedAnswer;

            return new NavigationGuidanceHint
            {
                CurrentScreen = decision.Context.ScreenDisplayName,
                VisibleNavigation = string.Join("; ", decision.Context.Evidence),
                NextClick = decision.NextStep.Instruction,
                NavigationSteps = [decision.NextStep.Instruction],
                IsOnCorrectTool = decision.Context.TargetVisible || decision.Context.ScreenType != MmcConsoleKind.Unknown,
                IsDestinationVisible = decision.Context.TargetVisible,
                UseLocalAnswer = decision.UseLocalResponse,
                LocalAnswer = localAnswer,
                ConfidenceLabel = decision.Context.ConfidenceCandidate,
                PromptDirective = decision.StructuredSummary
            };
        }
    }
}
