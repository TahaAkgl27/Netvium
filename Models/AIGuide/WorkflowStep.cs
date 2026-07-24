namespace Netvium.Models.AIGuide
{
    public sealed class WorkflowStep
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string Description { get; init; } = string.Empty;
        public List<string> EvidencePatterns { get; init; } = new();
        public string? NextLikelyStepHint { get; init; }
    }
}
