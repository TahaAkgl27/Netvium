using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class FreshQuestionCaptureTests
{
    [Fact]
    public void PrepareFreshQuestionCapture_ClearsScreenDerivedState_ButPreservesConversation()
    {
        var service = new AIGuideService();
        service.Conversation.Turns.Add(new ConversationTurn
        {
            Role = "user",
            Content = "Create a DHCP reservation"
        });
        service.SetRuntimeContext(new RuntimeContext());

        service.PrepareFreshQuestionCapture();

        Assert.Null(service.CurrentScreen);
        Assert.Null(service.LastAnalysis);
        Assert.Null(service.LastRuntimeContext);
        Assert.Single(service.Conversation.Turns);
        Assert.Equal("Create a DHCP reservation", service.Conversation.Turns[0].Content);
    }

    [Fact]
    public void PrepareFreshQuestionCapture_IsIdempotent_AndKeepsAllConversationTurns()
    {
        var service = new AIGuideService();
        service.Conversation.Turns.Add(new ConversationTurn
        {
            Role = "user",
            Content = "First screen"
        });
        service.Conversation.Turns.Add(new ConversationTurn
        {
            Role = "assistant",
            Content = "First answer"
        });

        service.PrepareFreshQuestionCapture();
        service.PrepareFreshQuestionCapture();

        Assert.Equal(2, service.Conversation.Turns.Count);
        Assert.Equal("First screen", service.Conversation.Turns[0].Content);
        Assert.Equal("First answer", service.Conversation.Turns[1].Content);
    }
}
