using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class ScreenGroundTruthTests
{
    [Theory]
    [InlineData("Doğru yerde miyim?")]
    [InlineData("doğru yerdemiyim")]
    [InlineData("Am I on the correct screen?")]
    public void ScreenVerificationQuestions_AreRecognized(string question)
    {
        Assert.Equal(
            VisualQuestionIntent.VerifyCurrentScreen,
            VisualQuestionIntentClassifier.Classify(question));
    }

    [Fact]
    public void VerificationPrompt_OmitsOldAssistantScreenClaim_ButKeepsUserGoal()
    {
        var request = CreateDhcpVerificationRequest();
        var prompt = ConfigurableGuidanceProvider.BuildUserPrompt(request, "{}");

        Assert.Contains("CURRENT screenshot detector identifies: DHCP", prompt, StringComparison.Ordinal);
        Assert.Contains("DHCP rezervasyonu", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Assistant: Active Directory Users and Computers", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationGuard_ReplacesContradictoryAducAnswer_WithCurrentDhcpScreen()
    {
        var wrongModelAnswer = new GuidanceResponse
        {
            RequestId = "screen-check",
            Success = true,
            DirectAnswer = "Evet, Active Directory Users and Computers ekranındasınız.",
            VisibilityNote = "Active Directory Users and Computers görünüyor."
        };

        var guarded = VisualGuidanceResponseGuard.Apply(
            wrongModelAnswer,
            CreateDhcpVerificationRequest());

        Assert.Contains("DHCP", guarded.DirectAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doğru", guarded.DirectAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Active Directory Users and Computers", guarded.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("DHCP", guarded.CurrentScreen);
    }

    [Fact]
    public void VerificationFollowUp_RestoresGoalWithoutAdvancingWorkflowStep()
    {
        var continuity = new ConversationContinuityService();
        var result = continuity.Resolve(new WorkflowDecisionInput
        {
            UserQuestion = "Doğru yerde miyim?",
            SessionState = new WorkflowSessionState
            {
                Intent = AdminUserIntent.DhcpReservation,
                Goal = WorkflowGoal.CreateReservation,
                WorkflowName = "dhcp-reservation",
                AnchorQuestion = "DHCP rezervasyonu oluşturacağım"
            }
        });

        Assert.True(result.IsContinuation);
        Assert.True(result.IsScreenVerification);
        Assert.False(result.ShouldAdvanceStep);
        Assert.Equal(AdminUserIntent.DhcpReservation, result.IntentOverride);
        Assert.Equal("DHCP rezervasyonu oluşturacağım", result.AnchorQuestion);
    }

    private static GuidanceRequest CreateDhcpVerificationRequest() =>
        new()
        {
            RequestId = "screen-check",
            UserQuestion = "Doğru yerde miyim?",
            Observation = new VisionObservation
            {
                Application = "DHCP",
                WindowTitle = "DHCP",
                Confidence = 0.95
            },
            PriorTurns =
            [
                new ConversationTurn { Role = "user", Content = "DHCP rezervasyonu oluşturacağım" },
                new ConversationTurn { Role = "assistant", Content = "Active Directory Users and Computers ekranına gidin." }
            ],
            VisualContext = new ScreenVisualContext
            {
                MmcConsole = MmcConsoleKind.Dhcp,
                MmcSnapIn = "DHCP",
                ActiveWindowTitle = "DHCP",
                OcrWindowTitle = "DHCP",
                OcrConfidence = 0.95
            },
            WorkflowContext = new WorkflowContext
            {
                ScreenType = MmcConsoleKind.Dhcp,
                ScreenDisplayName = "DHCP",
                Intent = AdminUserIntent.DhcpReservation,
                Goal = WorkflowGoal.CreateReservation,
                WorkflowName = "dhcp-reservation",
                IsWrongScreen = false,
                UseTurkish = true
            },
            IsFreshVisualContext = true,
            GuideMode = "ObserveOnly"
        };
}
