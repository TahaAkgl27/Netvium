using System.Text.Json.Serialization;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.Decision;
using Netvium.Services.AIGuide.ExperiencePack;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class Sprint5ExperienceTests
{
    private static ExperiencePackTestDocument Load() =>
        TestDataLoader.Load<ExperiencePackTestDocument>("experience-pack-tests.json");

    public static IEnumerable<object[]> ExperienceCases() =>
        Load().ExperienceTests.Select(t => new object[] { t });

    [Fact]
    public void ExperiencePack_LoadsAllDomains()
    {
        AIGuideTestHost.EnsureLoaded();
        var bundle = ExperiencePackHost.Current;
        Assert.True(bundle.AllDocuments.All(d => d.Entries.Count > 0));
    }

    [Theory]
    [MemberData(nameof(ExperienceCases))]
    public void ExperienceHints_MatchScenario(ExperienceTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var decision = TestCaseRunner.RunDecide(new DecideTestCase
        {
            UserQuestion = testCase.UserQuestion,
            ScreenKind = testCase.ScreenKind,
            ActiveWindowTitle = testCase.ActiveWindowTitle,
            OcrStatusBar = testCase.OcrStatusBar,
            StatusBarText = testCase.OcrStatusBar,
            SelectedListItem = testCase.SelectedListItem
        });

        Assert.NotEmpty(decision.ExperienceHints);
        foreach (var expected in testCase.ExpectHintContains)
        {
            Assert.Contains(decision.ExperienceHints,
                h => h.Text.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }

        if (testCase.ExpectAttentionInResponse == true)
        {
            var response = new WorkflowResponseGenerator().Generate(decision, "exp-test");
            var answer = response.FormattedAnswer ?? string.Empty;
            Assert.True(
                answer.Contains("Dikkat:", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("Attention:", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ExperienceHints_DoNotChangeConfidence()
    {
        AIGuideTestHost.EnsureLoaded();
        var decision = TestCaseRunner.RunDecide(new DecideTestCase
        {
            UserQuestion = "D:\\Shared\\IT Taha read izni vereceğim",
            ScreenKind = "FileExplorer",
            ActiveWindowTitle = "File Explorer",
            OcrStatusBar = "D:\\Shared\\IT",
            SelectedListItem = "IT"
        });

        Assert.NotEmpty(decision.ExperienceHints);
        Assert.False(string.IsNullOrWhiteSpace(decision.Context.ConfidenceCandidate));
    }
}

public sealed class ExperiencePackTestDocument
{
    [JsonPropertyName("experienceTests")]
    public List<ExperienceTestCase> ExperienceTests { get; set; } = [];
}

public sealed class ExperienceTestCase
{
    public string Name { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ScreenKind { get; set; } = string.Empty;
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string? OcrStatusBar { get; set; }
    public string? SelectedListItem { get; set; }
    public List<string> ExpectHintContains { get; set; } = [];
    public bool? ExpectAttentionInResponse { get; set; }
}
