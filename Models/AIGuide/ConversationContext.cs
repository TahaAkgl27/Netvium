namespace Netvium.Models.AIGuide
{
    public sealed class ConversationTurn
    {
        public required string Role { get; init; }
        public required string Content { get; init; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Read-only conversation state for future vision prompts. AI Guide never executes actions.
    /// </summary>
    public sealed class ConversationContext
    {
        public string? ActiveServerKey { get; set; }
        public List<ConversationTurn> Turns { get; } = new();
    }
}
