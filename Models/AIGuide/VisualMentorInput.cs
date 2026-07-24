namespace Netvium.Models.AIGuide
{
    public sealed class VisualMentorInput
    {
        public required VisionObservation Observation { get; init; }
        public VisionObservation? PreviousObservation { get; init; }
        public RuntimeContext? RuntimeContext { get; init; }
        public WorkflowState? WorkflowState { get; init; }
        public SmartScreenSummary? Summary { get; init; }
        public IReadOnlyList<ConversationTurn> ConversationHistory { get; init; } = Array.Empty<ConversationTurn>();
        public ObservationTimeline? PriorTimeline { get; init; }
    }
}
