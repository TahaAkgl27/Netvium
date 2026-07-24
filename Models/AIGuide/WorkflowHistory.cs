namespace Netvium.Models.AIGuide
{
    public sealed class WorkflowHistoryEntry
    {
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public string? Application { get; init; }
        public string? WindowTitle { get; init; }
        public string? StepName { get; init; }
        public string? WorkflowName { get; init; }
    }

    public sealed class WorkflowHistory
    {
        public List<WorkflowHistoryEntry> Entries { get; init; } = new();
    }
}
