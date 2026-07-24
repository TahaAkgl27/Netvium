namespace Netvium.Models.AIGuide
{
    public sealed class DetectedWorkflow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; }
        public List<string> ApplicationPatterns { get; init; } = new();
        public List<WorkflowStep> Steps { get; init; } = new();
    }
}
