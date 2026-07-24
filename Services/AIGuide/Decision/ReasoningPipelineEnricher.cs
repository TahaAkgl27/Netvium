using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.ExperiencePack;
using Netvium.Services.AIGuide.ExperiencePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Enriches decisions with evidence, reliability, and debug info. Does not override Confidence or mutate steps.</summary>
    public sealed class ReasoningPipelineEnricher
    {
        private readonly EvidenceEngine _evidenceEngine;
        private readonly GoalDetectionService _goalDetector;
        private readonly WorkflowStateDetectionService _stateDetector;
        private readonly AssumptionDetector _assumptionDetector;
        private readonly SelfVerificationEngine _selfVerification;
        private readonly ReliabilityScoreEngine _reliabilityEngine;
        private readonly ExperiencePackService _experiencePackService;

        public ReasoningPipelineEnricher(
            EvidenceEngine evidenceEngine,
            GoalDetectionService goalDetector,
            WorkflowStateDetectionService stateDetector,
            AssumptionDetector assumptionDetector,
            SelfVerificationEngine selfVerification,
            ReliabilityScoreEngine reliabilityEngine,
            ExperiencePackService experiencePackService)
        {
            _evidenceEngine = evidenceEngine;
            _goalDetector = goalDetector;
            _stateDetector = stateDetector;
            _assumptionDetector = assumptionDetector;
            _selfVerification = selfVerification;
            _reliabilityEngine = reliabilityEngine;
            _experiencePackService = experiencePackService;
        }
        public WorkflowDecisionResult Enrich(
            WorkflowDecisionInput input,
            WorkflowDecisionResult result,
            MmcConsoleKind screenType,
            string displayName,
            bool isWrongScreen)
        {
            var ctx = result.Context;
            var goal = _goalDetector.Detect(ctx.Intent, input.ResolvedQuestion, ctx.Target);

            var currentState = _stateDetector.DetectCurrentState(
                ctx,
                input.VisualContext,
                ctx.IsClarification,
                isWrongScreen || ctx.IsWrongScreen);

            var evidence = _evidenceEngine.Build(
                screenType,
                displayName,
                ctx.Intent,
                goal,
                ctx.Target,
                input.VisualContext,
                input.UserQuestion,
                ctx.WorkflowName,
                result.NextStep.StepId,
                currentState,
                ctx.TargetVisible,
                ctx.Evidence);

            var assumptions = _assumptionDetector.Analyze(ctx, goal, evidence, ctx.IsClarification);

            var verification = _selfVerification.Verify(
                ctx,
                result.NextStep,
                evidence,
                assumptions,
                isWrongScreen || ctx.IsWrongScreen);

            var nextState = _stateDetector.DetectNextState(
                currentState,
                result.NextStep,
                ctx.TargetVisible,
                ctx.IsClarification);

            var workflowMatched = ctx.WorkflowName is not ("generic" or "clarification" or "wrong-screen-guard");
            var experienceHints = _experiencePackService.SelectHints(new ExperienceSelectionInput
            {
                Intent = ctx.Intent,
                Goal = goal,
                WorkflowName = ctx.WorkflowName,
                ScreenType = screenType,
                CurrentState = currentState,
                UserQuestion = input.ResolvedQuestion,
                DetectedEventId = input.VisualContext?.DetectedEventId,
                UseTurkish = ctx.UseTurkish,
                IsClarification = ctx.IsClarification,
                IsWrongScreen = isWrongScreen || ctx.IsWrongScreen
            });

            var reliability = _reliabilityEngine.Evaluate(
                evidence,
                goal,
                workflowMatched,
                ctx.TargetVisible,
                verification.Passed,
                verification.HasContradiction,
                assumptions,
                isWrongScreen || ctx.IsWrongScreen);

            var evidenceStatements = evidence.All.Select(e => e.Statement).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var reason = BuildReasonForDecision(ctx, goal, currentState, nextState, verification, assumptions);

            var debugInfo = new ReasoningDebugInfo
            {
                ScreenType = ctx.ScreenDisplayName,
                Intent = ctx.Intent.ToString(),
                Goal = goal.ToString(),
                Workflow = ctx.WorkflowName,
                CurrentState = currentState.ToString(),
                NextState = nextState.ToString(),
                EvidenceCount = evidence.TotalCount,
                EvidenceCoverage = reliability.Coverage,
                AssumptionCount = assumptions.AssumptionCount,
                ReliabilityScore = reliability.Score,
                ReliabilityLabel = reliability.Label,
                ConfidenceLabel = ctx.ConfidenceCandidate,
                ReasonForDecision = reason,
                VerificationPassed = verification.Passed,
                VerificationIssues = verification.Issues
            };

            var enrichedContext = new WorkflowContext
            {
                ScreenType = ctx.ScreenType,
                ScreenDisplayName = ctx.ScreenDisplayName,
                Intent = ctx.Intent,
                Target = ctx.Target,
                Goal = goal,
                CurrentStep = result.NextStep.StepId,
                CurrentStepIndex = result.NextStep.StepIndex,
                TotalSteps = ctx.TotalSteps,
                TargetVisible = ctx.TargetVisible,
                WorkflowName = ctx.WorkflowName,
                CurrentState = currentState,
                NextState = nextState,
                ConfidenceCandidate = ctx.ConfidenceCandidate,
                ConfidenceScore = ctx.ConfidenceScore,
                ReliabilityScore = reliability.Score,
                ReliabilityLabel = reliability.Label,
                UseTurkish = ctx.UseTurkish,
                ScreenObservation = ctx.ScreenObservation,
                Evidence = evidenceStatements,
                StructuredEvidence = evidence,
                Assumptions = assumptions,
                VisibleObjects = ctx.VisibleObjects,
                AllowedScreens = ctx.AllowedScreens,
                IsWrongScreen = ctx.IsWrongScreen,
                IsConsumerApp = ctx.IsConsumerApp,
                MissingRequiredTarget = ctx.MissingRequiredTarget,
                ShouldRefuse = ctx.ShouldRefuse,
                ShouldContinue = ctx.ShouldContinue,
                ShouldAskQuestion = ctx.ShouldAskQuestion,
                IsClarification = ctx.IsClarification,
                MissingFields = ctx.MissingFields,
                ClarificationQuestion = ctx.ClarificationQuestion,
                WrongObjectDetected = ctx.WrongObjectDetected,
                DebugInfo = debugInfo
            };

            return new WorkflowDecisionResult
            {
                Context = enrichedContext,
                NextStep = result.NextStep,
                HasWorkflow = result.HasWorkflow,
                UseLocalResponse = result.UseLocalResponse,
                StructuredSummary = result.StructuredSummary,
                ShouldRefuse = result.ShouldRefuse,
                ShouldContinue = result.ShouldContinue,
                ShouldAskQuestion = result.ShouldAskQuestion,
                ManualStepInstructions = result.ManualStepInstructions,
                DebugInfo = debugInfo,
                Evidence = evidence,
                Reliability = reliability,
                Verification = verification,
                ExperienceHints = experienceHints
            };
        }

        private static string BuildReasonForDecision(
            WorkflowContext ctx,
            WorkflowGoal goal,
            WorkflowScreenState currentState,
            WorkflowScreenState nextState,
            SelfVerificationResult verification,
            AssumptionAnalysis assumptions)
        {
            var parts = new List<string>
            {
                $"Intent={ctx.Intent}",
                $"Goal={goal}",
                $"Workflow={ctx.WorkflowName}",
                $"State={currentState}->{nextState}",
                $"Confidence={ctx.ConfidenceCandidate}"
            };

            if (assumptions.AssumptionCount > 0)
                parts.Add($"Assumptions={assumptions.AssumptionCount}");

            if (!verification.Passed)
                parts.Add($"VerificationIssues={verification.Issues.Count}");

            return string.Join("; ", parts);
        }
    }
}

