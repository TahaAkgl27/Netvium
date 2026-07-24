using Xunit;

namespace Netvium.AIGuide.Tests;

public class Sprint2GuardTests
{
    public static IEnumerable<object[]> ScreenCompatibilityCases()
    {
        var doc = TestDataLoader.Load<Sprint2GuardTestDocument>("sprint2-guard-tests.json");
        return doc.ScreenCompatibility.Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> ConfidenceCases()
    {
        var doc = TestDataLoader.Load<Sprint2GuardTestDocument>("sprint2-guard-tests.json");
        return doc.Confidence.Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> DecideCases()
    {
        var doc = TestDataLoader.Load<Sprint2GuardTestDocument>("sprint2-guard-tests.json");
        return doc.Decide.Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> HallucinationCases()
    {
        var doc = TestDataLoader.Load<Sprint2GuardTestDocument>("sprint2-guard-tests.json");
        return doc.Hallucination.Select(c => new object[] { c });
    }

    [Theory]
    [MemberData(nameof(ScreenCompatibilityCases))]
    public void ScreenIntentCompatibility_FromDataset(ScreenCompatibilityTestCase testCase) =>
        TestCaseRunner.RunScreenCompatibility(testCase);

    [Theory]
    [MemberData(nameof(ConfidenceCases))]
    public void ConfidenceEngine_FromDataset(ConfidenceTestCase testCase) =>
        TestCaseRunner.RunConfidence(testCase);

    [Theory]
    [MemberData(nameof(DecideCases))]
    public void Decide_FromDataset(DecideTestCase testCase) =>
        TestCaseRunner.AssertDecide(testCase);

    [Theory]
    [MemberData(nameof(HallucinationCases))]
    public void HallucinationGuard_FromDataset(HallucinationTestCase testCase) =>
        TestCaseRunner.RunHallucination(testCase);
}
