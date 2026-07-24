using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class QaScenarioTestDocument
{
    public WorkflowRegistryExpectation WorkflowRegistry { get; init; } = new();
    public List<DecideTestCase> Decide { get; init; } = [];
    public List<ResponseGeneratorTestCase> ResponseGenerator { get; init; } = [];
}

public sealed class WorkflowRegistryExpectation
{
    public int ExpectMinGuides { get; init; }
    public List<string> ExpectWorkflowIds { get; init; } = [];
}

public sealed class ResponseGeneratorTestCase
{
    public string Name { get; init; } = string.Empty;
    public string UserQuestion { get; init; } = string.Empty;
    public string ActiveWindowTitle { get; init; } = string.Empty;
    public string ScreenKind { get; init; } = string.Empty;
    public bool ExpectSingleManualStep { get; init; }
}

public class WorkflowDecisionEngineTests
{
    private static QaScenarioTestDocument Doc => TestDataLoader.Load<QaScenarioTestDocument>("qa-scenarios.json");

    [Fact]
    public void WorkflowGuideRegistry_LoadsFromKnowledgePack()
    {
        AIGuideTestHost.EnsureLoaded();
        var guides = AIGuideCompositionRoot.CreateWorkflowGuidesFromPack();
        var expectation = Doc.WorkflowRegistry;

        Assert.True(guides.Count >= expectation.ExpectMinGuides);
        foreach (var id in expectation.ExpectWorkflowIds)
            Assert.Contains(guides, g => g.WorkflowName == id);
    }

    public static IEnumerable<object[]> DecideCases() =>
        Doc.Decide.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(DecideCases))]
    public void Decide_FromDataset(DecideTestCase testCase)
    {
        TestCaseRunner.AssertDecide(testCase);
        var result = TestCaseRunner.RunDecide(testCase);
        Assert.False(string.IsNullOrWhiteSpace(result.NextStep.Instruction));

        if (testCase.Name == "ReturnsOnlyOneNextStep_NotFullWorkflow")
            Assert.True(result.Context.TotalSteps > 1);
    }

    public static IEnumerable<object[]> ResponseGeneratorCases() =>
        Doc.ResponseGenerator.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(ResponseGeneratorCases))]
    public void ResponseGenerator_FromDataset(ResponseGeneratorTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var decision = TestCaseRunner.RunDecide(new DecideTestCase
        {
            UserQuestion = testCase.UserQuestion,
            ActiveWindowTitle = testCase.ActiveWindowTitle,
            ScreenKind = testCase.ScreenKind
        });

        var response = new WorkflowResponseGenerator().Generate(decision, "test-1");
        Assert.True(response.Success);
        if (testCase.ExpectSingleManualStep)
            Assert.Single(response.ManualSteps);
        Assert.False(string.IsNullOrWhiteSpace(response.FormattedAnswer));
    }
}
