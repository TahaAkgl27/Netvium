using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class ConfidenceCalibrationTests
{
    [Fact]
    public void RecognizedScreenWithExploratoryQuestion_IsMedium()
    {
        var result = new ConfidenceEngine().Evaluate(new ConfidenceInput
        {
            ScreenCorrect = true,
            IsUnknownScreen = false,
            IntentDetected = false,
            IntentUnknown = true,
            IsExploratoryQuestion = true,
            WorkflowMatched = false,
            IsGenericWorkflow = true
        });
        Assert.Equal("Medium", result.Label);
    }

    [Fact]
    public void UnknownScreenAndUnknownIntent_RemainsLow()
    {
        var result = new ConfidenceEngine().Evaluate(new ConfidenceInput
        {
            ScreenCorrect = false,
            IsUnknownScreen = true,
            IntentDetected = false,
            IntentUnknown = true,
            WorkflowMatched = false,
            IsGenericWorkflow = true
        });
        Assert.Equal("Low", result.Label);
    }
}
