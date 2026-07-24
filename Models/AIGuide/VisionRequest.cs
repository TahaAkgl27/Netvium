namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Prepared vision request payload. Sprint 1 does not transmit this to any external service.
    /// </summary>
    public sealed class VisionRequest
    {
        public required string RequestId { get; init; }
        public required ScreenContext Screen { get; init; }
        public required ConversationContext Conversation { get; init; }
        public string? UserPrompt { get; init; }
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

        /// <summary>Explicit safety flag — AI Guide is observation-only.</summary>
        public bool ReadOnlyAnalysis { get; init; } = true;

        /// <summary>True when this request must ignore all prior screen/conversation context.</summary>
        public bool IsFreshAnalysis { get; init; }
    }
}
