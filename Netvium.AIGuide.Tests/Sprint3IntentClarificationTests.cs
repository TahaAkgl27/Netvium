using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public class Sprint3IntentClarificationTests
{
    public static IEnumerable<object[]> IntentDetectionCases()
    {
        var doc = TestDataLoader.Load<Sprint3IntentTestDocument>("sprint3-intent-tests.json");
        return doc.IntentDetection.Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> DecideCases()
    {
        var doc = TestDataLoader.Load<Sprint3IntentTestDocument>("sprint3-intent-tests.json");
        return doc.Decide.Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> ConfidenceCases()
    {
        var doc = TestDataLoader.Load<Sprint3IntentTestDocument>("sprint3-intent-tests.json");
        return doc.Confidence.Select(c => new object[] { c });
    }

    [Theory]
    [MemberData(nameof(IntentDetectionCases))]
    public void IntentDetection_FromDataset(IntentDetectionTestCase testCase)
    {
        AIGuideTestHost.EnsureLoaded();
        var svc = new IntentDetectionService();
        var intent = svc.Detect(testCase.UserQuestion, null);
        Assert.Equal(Enum.Parse<AdminUserIntent>(testCase.ExpectIntent), intent);
    }

    [Theory]
    [MemberData(nameof(DecideCases))]
    public void Decide_FromDataset(DecideTestCase testCase) =>
        TestCaseRunner.AssertDecide(testCase);

    [Theory]
    [MemberData(nameof(ConfidenceCases))]
    public void ConfidenceEngine_FromDataset(ConfidenceTestCase testCase) =>
        TestCaseRunner.RunConfidence(testCase);
}
