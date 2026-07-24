using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

internal static class TestCaseRunner
{
    public static void RunScreenCompatibility(ScreenCompatibilityTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var svc = new ScreenIntentCompatibilityService();
        var intent = Enum.Parse<AdminUserIntent>(testCase.Intent);
        var screenKind = Enum.Parse<MmcConsoleKind>(testCase.ScreenKind);
        var result = svc.Evaluate(intent, screenKind, testCase.DisplayName);

        Assert.Equal(testCase.ExpectCompatible, result.IsCompatible);
        if (testCase.ExpectConsumerApp.HasValue)
            Assert.Equal(testCase.ExpectConsumerApp.Value, result.IsConsumerApp);
    }

    public static void RunConfidence(ConfidenceTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var engine = new ConfidenceEngine();
        var eval = engine.Evaluate(new ConfidenceInput
        {
            IsWrongScreen = testCase.IsWrongScreen,
            ScreenCorrect = testCase.ScreenCorrect,
            IntentDetected = testCase.IntentDetected,
            WorkflowMatched = testCase.WorkflowMatched,
            TargetVisible = testCase.TargetVisible,
            TargetVerified = testCase.TargetVerified,
            HallucinationGuardTriggered = testCase.HallucinationGuardTriggered,
            IsFolderPermissionIntent = testCase.IsFolderPermissionIntent
        });

        Assert.Equal(testCase.ExpectLabel, eval.Label);
        if (testCase.ExpectScore.HasValue)
            Assert.Equal(testCase.ExpectScore.Value, eval.Score);
    }

    public static WorkflowDecisionResult RunDecide(DecideTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var engine = AIGuideCompositionRoot.CreateDecisionEngine();
        return engine.Decide(new WorkflowDecisionInput
        {
            UserQuestion = testCase.UserQuestion,
            VisualContext = new ScreenVisualContext
            {
                ActiveWindowTitle = testCase.ActiveWindowTitle,
                MmcConsole = Enum.Parse<MmcConsoleKind>(testCase.ScreenKind),
                OcrStatusBar = testCase.OcrStatusBar ?? string.Empty,
                StatusBarText = testCase.StatusBarText ?? string.Empty,
                SelectedListItem = testCase.SelectedListItem ?? string.Empty,
                OcrSelectedRow = testCase.SelectedListItem ?? string.Empty
            }
        });
    }

    public static void AssertDecide(DecideTestCase testCase)
    {
        var result = RunDecide(testCase);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectWorkflowName))
            Assert.Equal(testCase.ExpectWorkflowName, result.Context.WorkflowName);

        if (testCase.ExpectShouldRefuse.HasValue)
            Assert.Equal(testCase.ExpectShouldRefuse.Value, result.ShouldRefuse);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectConfidence))
            Assert.Equal(testCase.ExpectConfidence, result.Context.ConfidenceCandidate);

        if (testCase.ExpectMinManualSteps.HasValue)
            Assert.True(result.ManualStepInstructions.Count >= testCase.ExpectMinManualSteps.Value);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectNextStepContains))
            Assert.Contains(testCase.ExpectNextStepContains, result.NextStep.Instruction, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectObservationContains))
        {
            Assert.True(
                result.Context.ScreenObservation.Contains(testCase.ExpectObservationContains, StringComparison.OrdinalIgnoreCase)
                || result.Context.ScreenObservation.Contains("not visible", StringComparison.OrdinalIgnoreCase),
                $"Observation should mention hidden target. Actual: {result.Context.ScreenObservation}");
        }

        if (!string.IsNullOrWhiteSpace(testCase.ExpectIntent))
            Assert.Equal(Enum.Parse<AdminUserIntent>(testCase.ExpectIntent), result.Context.Intent);

        if (testCase.ExpectShouldAskQuestion.HasValue)
            Assert.Equal(testCase.ExpectShouldAskQuestion.Value, result.ShouldAskQuestion);
    }

    public static void RunHallucination(HallucinationTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var guard = new HallucinationGuard();
        var ctx = new WorkflowContext
        {
            Intent = AdminUserIntent.FolderPermission,
            TargetVisible = testCase.TargetVisible,
            Target = new WorkflowTarget { Path = testCase.TargetPath, FolderName = testCase.FolderName },
            UseTurkish = true
        };

        var result = guard.Validate(ctx, testCase.Instruction, [testCase.Instruction]);
        Assert.Equal(testCase.ExpectPassed, result.Passed);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectSanitizedContains))
            Assert.Contains(testCase.ExpectSanitizedContains, result.SanitizedInstruction, StringComparison.OrdinalIgnoreCase);
    }
}
