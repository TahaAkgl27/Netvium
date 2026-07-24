using System.Text.Json.Serialization;
using Netvium.Models.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class Sprint4ExcellenceTests
{
    private static Sprint4TestDocument Load() =>
        TestDataLoader.Load<Sprint4TestDocument>("sprint4-excellence-tests.json");

    public static IEnumerable<object[]> EvidenceCases() =>
        Load().EvidenceTests.Select(t => new object[] { t });

    public static IEnumerable<object[]> GoalCases() =>
        Load().GoalTests.Select(t => new object[] { t });

    public static IEnumerable<object[]> ClarificationCases() =>
        Load().ClarificationTests.Select(t => new object[] { t });

    public static IEnumerable<object[]> ReliabilityCases() =>
        Load().ReliabilityTests.Select(t => new object[] { t });

    public static IEnumerable<object[]> VerificationCases() =>
        Load().VerificationTests.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(EvidenceCases))]
    public void EvidenceEngine_ProducesStructuredEvidence(EvidenceTestCase testCase)
    {
        var result = Run(testCase.UserQuestion, testCase.ScreenKind, testCase.ActiveWindowTitle,
            testCase.OcrStatusBar, testCase.SelectedListItem);
        Assert.True(result.Evidence!.TotalCount >= testCase.ExpectMinEvidenceCount,
            $"Expected >= {testCase.ExpectMinEvidenceCount} evidence items, got {result.Evidence.TotalCount}");

        if (!string.IsNullOrWhiteSpace(testCase.ExpectGoal))
            Assert.Equal(Enum.Parse<WorkflowGoal>(testCase.ExpectGoal), result.Context.Goal);
    }

    [Theory]
    [MemberData(nameof(GoalCases))]
    public void GoalDetection_MapsIntentToGoal(GoalTestCase testCase)
    {
        var result = Run(testCase.UserQuestion, testCase.ScreenKind, testCase.ActiveWindowTitle,
            null, null);

        Assert.Equal(Enum.Parse<WorkflowGoal>(testCase.ExpectGoal), result.Context.Goal);
    }

    [Theory]
    [MemberData(nameof(ClarificationCases))]
    public void Clarification_AsksMinimumField(ClarificationTestCase testCase)
    {
        var result = Run(testCase.UserQuestion, testCase.ScreenKind, testCase.ActiveWindowTitle,
            testCase.OcrStatusBar, null);

        if (testCase.ExpectShouldAskQuestion.HasValue)
            Assert.Equal(testCase.ExpectShouldAskQuestion.Value, result.ShouldAskQuestion);

        if (testCase.ExpectSingleMissingField == true)
            Assert.Single(result.Context.MissingFields);

        if (testCase.ExpectMissingFieldCount.HasValue)
            Assert.Equal(testCase.ExpectMissingFieldCount.Value, result.Context.MissingFields.Count);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectQuestionContains))
            Assert.Contains(testCase.ExpectQuestionContains, result.Context.ClarificationQuestion,
                StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ReliabilityCases))]
    public void Reliability_ScoresHiddenTargetsLower(ReliabilityTestCase testCase)
    {
        var result = Run(testCase.UserQuestion, testCase.ScreenKind, testCase.ActiveWindowTitle,
            testCase.OcrStatusBar, null);

        Assert.NotNull(result.Reliability);
        Assert.True(result.Reliability!.Score <= testCase.ExpectMaxReliability,
            $"Expected reliability <= {testCase.ExpectMaxReliability}, got {result.Reliability.Score}");
    }

    [Theory]
    [MemberData(nameof(VerificationCases))]
    public void SelfVerification_RunsWithEvidence(VerificationTestCase testCase)
    {
        var result = Run(testCase.UserQuestion, testCase.ScreenKind, testCase.ActiveWindowTitle,
            testCase.OcrStatusBar, testCase.SelectedListItem);

        Assert.NotNull(result.Verification);
        Assert.NotNull(result.DebugInfo);
        Assert.True(result.Evidence!.TotalCount >= testCase.ExpectMinEvidenceCount);

        if (testCase.ExpectVerificationPresent == true)
            Assert.NotNull(result.Verification);
    }

    [Fact]
    public void ReasoningPipeline_ProducesDebugInfo()
    {
        var result = Run(
            "D:\\Shared\\IT klasörüne ahmet için read ver",
            "FileExplorer",
            "File Explorer",
            "D:\\Shared",
            "IT");

        Assert.NotNull(result.DebugInfo);
        Assert.True(result.DebugInfo!.EvidenceCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.DebugInfo.ReasonForDecision));
        Assert.NotEqual(WorkflowScreenState.Unknown, result.Context.CurrentState);
    }

    private static WorkflowDecisionResult Run(
        string question,
        string screenKind,
        string title,
        string? statusBar = null,
        string? selected = null) =>
        TestCaseRunner.RunDecide(new DecideTestCase
        {
            UserQuestion = question,
            ScreenKind = screenKind,
            ActiveWindowTitle = title,
            OcrStatusBar = statusBar,
            StatusBarText = statusBar,
            SelectedListItem = selected
        });
}

public sealed class Sprint4TestDocument
{
    [JsonPropertyName("evidenceTests")]
    public List<EvidenceTestCase> EvidenceTests { get; set; } = [];

    [JsonPropertyName("goalTests")]
    public List<GoalTestCase> GoalTests { get; set; } = [];

    [JsonPropertyName("clarificationTests")]
    public List<ClarificationTestCase> ClarificationTests { get; set; } = [];

    [JsonPropertyName("reliabilityTests")]
    public List<ReliabilityTestCase> ReliabilityTests { get; set; } = [];

    [JsonPropertyName("verificationTests")]
    public List<VerificationTestCase> VerificationTests { get; set; } = [];
}

public sealed class EvidenceTestCase
{
    public string Name { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ScreenKind { get; set; } = string.Empty;
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string? OcrStatusBar { get; set; }
    public string? SelectedListItem { get; set; }
    public int ExpectMinEvidenceCount { get; set; }
    public string? ExpectGoal { get; set; }
}

public sealed class GoalTestCase
{
    public string Name { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ScreenKind { get; set; } = string.Empty;
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string ExpectGoal { get; set; } = string.Empty;
}

public sealed class ClarificationTestCase
{
    public string Name { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ScreenKind { get; set; } = string.Empty;
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string? OcrStatusBar { get; set; }
    public bool? ExpectShouldAskQuestion { get; set; }
    public bool? ExpectSingleMissingField { get; set; }
    public int? ExpectMissingFieldCount { get; set; }
    public string? ExpectQuestionContains { get; set; }
}

public sealed class ReliabilityTestCase
{
    public string Name { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ScreenKind { get; set; } = string.Empty;
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string? OcrStatusBar { get; set; }
    public int ExpectMaxReliability { get; set; }
}

public sealed class VerificationTestCase
{
    public string Name { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ScreenKind { get; set; } = string.Empty;
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string? OcrStatusBar { get; set; }
    public string? SelectedListItem { get; set; }
    public bool? ExpectVerificationPresent { get; set; }
    public int ExpectMinEvidenceCount { get; set; }
}
