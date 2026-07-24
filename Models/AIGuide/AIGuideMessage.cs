namespace Netvium.Models.AIGuide
{
    public sealed class AIGuideMessage
    {
        public required string Role { get; init; }
        public required string Content { get; init; }
        public string? Confidence { get; init; }
        public List<GuidanceEvidence> Evidence { get; init; } = new();
        public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
        public bool HasEvidence => Evidence.Count > 0;
        public bool HasConfidence => !string.IsNullOrWhiteSpace(Confidence);
    }
}
