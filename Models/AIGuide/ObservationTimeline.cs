namespace Netvium.Models.AIGuide
{
    public enum TimelineEventType
    {
        ScreenAnalyzed,
        WorkflowDetected,
        ApplicationChanged,
        RoleDetected,
        SummaryGenerated,
        UserQuestion,
        AnswerGenerated,
        DialogOpened,
        DialogClosed,
        SelectionChanged,
        WindowChanged,
        WarningRaised
    }

    public sealed class TimelineEntry
    {
        public TimelineEventType EventType { get; init; }
        public required string Message { get; init; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    }

    public sealed class ObservationTimeline
    {
        public List<TimelineEntry> Entries { get; init; } = new();
    }
}
