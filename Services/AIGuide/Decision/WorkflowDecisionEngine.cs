using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    public interface IWorkflowDecisionEngine
    {
        WorkflowDecisionResult Decide(WorkflowDecisionInput input);
    }

    /// <summary>
    /// Deterministic multi-stage reasoning pipeline — no LLM. Unit-testable.
    /// Screen → OCR context → Evidence → Intent → Goal → Compatibility → Clarification →
    /// Workflow → State → Draft step → Verification → Reliability → Final response
    /// </summary>
    public sealed class WorkflowDecisionEngine : IWorkflowDecisionEngine
    {
        private readonly ScreenClassificationService _screenClassifier;
        private readonly IntentDetectionService _intentDetector;
        private readonly TargetExtractionService _targetExtractor;
        private readonly WorkflowGuideRegistry _registry;
        private readonly ScreenIntentCompatibilityService _compatibility;
        private readonly VisibleObjectsExtractor _visibleObjects;
        private readonly ConfidenceEngine _confidenceEngine;
        private readonly HallucinationGuard _hallucinationGuard;
        private readonly ClarificationEngine _clarificationEngine;
        private readonly WrongObjectDetector _wrongObjectDetector;
        private readonly WorkflowConsistencyValidator _workflowConsistency;
        private readonly ReasoningPipelineEnricher _reasoningEnricher;
        private readonly ConversationContinuityService _continuityService;
        private readonly WorkflowStepProgressionService _stepProgression;

        public WorkflowDecisionEngine(
            ScreenClassificationService screenClassifier,
            IntentDetectionService intentDetector,
            TargetExtractionService targetExtractor,
            WorkflowGuideRegistry registry,
            ScreenIntentCompatibilityService compatibility,
            VisibleObjectsExtractor visibleObjects,
            ConfidenceEngine confidenceEngine,
            HallucinationGuard hallucinationGuard,
            ClarificationEngine clarificationEngine,
            WrongObjectDetector wrongObjectDetector,
            WorkflowConsistencyValidator workflowConsistency,
            ReasoningPipelineEnricher reasoningEnricher,
            ConversationContinuityService continuityService,
            WorkflowStepProgressionService stepProgression)
        {
            _screenClassifier = screenClassifier;
            _intentDetector = intentDetector;
            _targetExtractor = targetExtractor;
            _registry = registry;
            _compatibility = compatibility;
            _visibleObjects = visibleObjects;
            _confidenceEngine = confidenceEngine;
            _hallucinationGuard = hallucinationGuard;
            _clarificationEngine = clarificationEngine;
            _wrongObjectDetector = wrongObjectDetector;
            _workflowConsistency = workflowConsistency;
            _reasoningEnricher = reasoningEnricher;
            _continuityService = continuityService;
            _stepProgression = stepProgression;
        }

        public WorkflowDecisionResult Decide(WorkflowDecisionInput input)
        {
            var continuity = _continuityService.Resolve(input);
            var effectiveQuestion = continuity.AnchorQuestion ?? input.ResolvedQuestion;
            var enrichedInput = new WorkflowDecisionInput
            {
                UserQuestion = input.UserQuestion,
                VisualContext = input.VisualContext,
                Observation = input.Observation,
                PriorTurns = input.PriorTurns,
                SessionState = input.SessionState,
                EffectiveQuestion = effectiveQuestion
            };

            var useTurkish = AIGuideLanguage.UseTurkishForResponse(effectiveQuestion);
            var (screenType, displayName) = _screenClassifier.Classify(input.VisualContext);

            AdminUserIntent intent;
            if (continuity.IsContinuation)
                intent = continuity.IntentOverride
                    ?? _intentDetector.Detect(effectiveQuestion, input.VisualContext);
            else
                intent = _intentDetector.Detect(input.UserQuestion, input.VisualContext);

            var extractedTarget = _targetExtractor.Extract(intent, effectiveQuestion, useTurkish, input.VisualContext);
            var target = ConversationContinuityService.MergeTargets(extractedTarget, continuity.TargetOverride);
            var visibleObjects = _visibleObjects.Extract(input.VisualContext, target);

            var compatibility = _compatibility.Evaluate(intent, screenType, displayName);
            if (!compatibility.IsCompatible && intent != AdminUserIntent.Unknown)
            {
                var wrongScreen = WrongScreenGuardBuilder.Build(
                    intent, screenType, displayName, target, useTurkish,
                    compatibility, visibleObjects, _confidenceEngine, screenType == MmcConsoleKind.IisManager);

                return Finalize(enrichedInput, wrongScreen, screenType, displayName, isWrongScreen: true);
            }

            var clarification = _clarificationEngine.Evaluate(
                intent, target, input.VisualContext, screenType, useTurkish, effectiveQuestion);

            if (clarification.Required && !continuity.IsContinuation)
            {
                var clarify = ClarificationResultBuilder.Build(
                    intent, screenType, displayName, target, clarification,
                    useTurkish, visibleObjects, _confidenceEngine);

                return Finalize(enrichedInput, clarify, screenType, displayName, isWrongScreen: false);
            }

            var guide = _registry.Resolve(intent, screenType);
            var buildInput = new WorkflowGuideBuildInput
            {
                UserQuestion = effectiveQuestion,
                VisualContext = input.VisualContext,
                ScreenType = screenType,
                ScreenDisplayName = displayName,
                Intent = intent,
                Target = target,
                UseTurkish = useTurkish
            };

            var context = guide.BuildContext(buildInput);
            var nextStep = guide.GetNextStep(context);

            var continuationStep = _stepProgression.TryGetContinuationStep(continuity, context, input.SessionState);
            if (continuationStep != null)
                nextStep = continuationStep;

            if (!_workflowConsistency.IsStepConsistent(guide.WorkflowName, nextStep.StepId, intent))
            {
                nextStep = WorkflowGuideSupport.Step("generic", 1,
                    useTurkish
                        ? "Mevcut ekranda görünen seçeneklerle devam edin."
                        : "Continue using the options visible on the current screen.");
            }

            var wrongObject = _wrongObjectDetector.Detect(
                intent, target, input.VisualContext, context.TargetVisible, useTurkish);

            if (wrongObject.Detected)
            {
                context = new WorkflowContext
                {
                    ScreenType = context.ScreenType,
                    ScreenDisplayName = context.ScreenDisplayName,
                    Intent = context.Intent,
                    Target = context.Target,
                    CurrentStepIndex = context.CurrentStepIndex,
                    TotalSteps = context.TotalSteps,
                    TargetVisible = context.TargetVisible,
                    WorkflowName = context.WorkflowName,
                    UseTurkish = context.UseTurkish,
                    ScreenObservation = wrongObject.Message,
                    Evidence = context.Evidence,
                    WrongObjectDetected = true
                };

                if (target.HasUserProvidedPath && !context.TargetVisible)
                {
                    nextStep = WorkflowGuideSupport.Step("navigate-target",
                        1,
                        WorkflowStepRenderer.RenderNavigateToPath(target.Path, useTurkish));
                }
            }

            var guardResult = _hallucinationGuard.Validate(
                context,
                nextStep.Instruction,
                [nextStep.Instruction],
                input.VisualContext?.SelectedListItem);

            if (!guardResult.Passed)
            {
                nextStep = WorkflowGuideSupport.Step(
                    nextStep.StepId,
                    nextStep.StepIndex,
                    guardResult.SanitizedInstruction,
                    nextStep.IsComplete);
            }

            var isGeneric = guide.WorkflowName == "generic";
            var screenCorrect = compatibility.IsCompatible || intent == AdminUserIntent.Unknown;
            var onSmFolder = context.ScreenType == MmcConsoleKind.ServerManager
                             && intent == AdminUserIntent.FolderPermission;

            var targetVisibleAndClear = context.TargetVisible
                && intent == AdminUserIntent.FolderPermission
                && !string.IsNullOrWhiteSpace(target.UserName)
                && (target.HasUserProvidedPath || target.TargetFromSelection);

            var hasRecognizedScreen = screenType != MmcConsoleKind.Unknown
                || !string.IsNullOrWhiteSpace(displayName)
                && !displayName.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
            var confidence = _confidenceEngine.Evaluate(new ConfidenceInput
            {
                IsWrongScreen = false,
                IsUnknownScreen = !hasRecognizedScreen,
                ScreenCorrect = screenCorrect && hasRecognizedScreen,
                IntentDetected = intent != AdminUserIntent.Unknown,
                IntentUnknown = intent == AdminUserIntent.Unknown,
                IsExploratoryQuestion = IsExploratoryScreenQuestion(input.UserQuestion),
                WorkflowMatched = !isGeneric,
                TargetVisible = context.TargetVisible,
                TargetVerified = target.HasUserProvidedPath,
                TargetSelectedButNotVerified = target.TargetFromSelection && !target.HasUserProvidedPath,
                TargetVisibleAndSelectedAndIntentClear = targetVisibleAndClear,
                MissingRequiredTarget = false,
                IsGenericWorkflow = isGeneric,
                IsServerManagerFolderPermission = onSmFolder,
                HallucinationGuardTriggered = !guardResult.Passed,
                WrongObjectDetected = wrongObject.Detected,
                IsFolderPermissionIntent = intent == AdminUserIntent.FolderPermission
            });

            context = new WorkflowContext
            {
                ScreenType = context.ScreenType,
                ScreenDisplayName = context.ScreenDisplayName,
                Intent = context.Intent,
                Target = context.Target,
                CurrentStep = nextStep.StepId,
                CurrentStepIndex = nextStep.StepIndex,
                TotalSteps = context.TotalSteps,
                TargetVisible = context.TargetVisible,
                WorkflowName = context.WorkflowName,
                ConfidenceCandidate = confidence.Label,
                ConfidenceScore = confidence.Score,
                UseTurkish = context.UseTurkish,
                ScreenObservation = context.ScreenObservation,
                Evidence = context.Evidence,
                VisibleObjects = visibleObjects,
                AllowedScreens = compatibility.AllowedScreenLabels,
                WrongObjectDetected = wrongObject.Detected,
                ShouldRefuse = false,
                ShouldContinue = true,
                ShouldAskQuestion = false
            };

            var draft = new WorkflowDecisionResult
            {
                HasWorkflow = intent != AdminUserIntent.Unknown || !isGeneric,
                UseLocalResponse = !isGeneric,
                ShouldRefuse = false,
                ShouldContinue = true,
                ShouldAskQuestion = false,
                Context = context,
                NextStep = nextStep,
                ManualStepInstructions = [nextStep.Instruction],
                StructuredSummary = BuildStructuredSummary(context, nextStep)
            };

            return Finalize(enrichedInput, draft, screenType, displayName, isWrongScreen: false);
        }

        private WorkflowDecisionResult Finalize(
            WorkflowDecisionInput input,
            WorkflowDecisionResult result,
            MmcConsoleKind screenType,
            string displayName,
            bool isWrongScreen) =>
            _reasoningEnricher.Enrich(input, result, screenType, displayName, isWrongScreen);

        private static bool IsExploratoryScreenQuestion(string? question)
        {
            var text = QuestionTextNormalizer.Normalize(question);
            return text.Contains("bu ekranda ne yapabilirim", StringComparison.Ordinal)
                || text.Contains("bu ekran nedir", StringComparison.Ordinal)
                || text.Contains("hangi ekrandayim", StringComparison.Ordinal)
                || text.Contains("what can i do on this screen", StringComparison.Ordinal)
                || text.Contains("what is this screen", StringComparison.Ordinal);
        }
        private static string BuildStructuredSummary(WorkflowContext ctx, WorkflowStepResult step) =>
            $"Workflow={ctx.WorkflowName}; Step={step.StepId}; Intent={ctx.Intent}; Screen={ctx.ScreenDisplayName}";
    }
}
