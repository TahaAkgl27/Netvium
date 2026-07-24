namespace Netvium.AIGuide.Tests;

public sealed class Sprint3IntentTestDocument
{
    public List<IntentDetectionTestCase> IntentDetection { get; init; } = [];
    public List<DecideTestCase> Decide { get; init; } = [];
    public List<ConfidenceTestCase> Confidence { get; init; } = [];
}

public sealed class IntentDetectionTestCase
{
    public string Name { get; init; } = string.Empty;
    public string UserQuestion { get; init; } = string.Empty;
    public string ExpectIntent { get; init; } = string.Empty;
}
