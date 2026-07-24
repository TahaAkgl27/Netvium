namespace Netvium.Models.AIGuide
{
    public sealed class WorkflowDetectionInput
    {
        public required VisionObservation CurrentObservation { get; init; }
        public VisionObservation? PreviousObservation { get; init; }
        public RuntimeContext? RuntimeContext { get; init; }
        public SmartScreenSummary? CurrentSummary { get; init; }
        public IReadOnlyList<ConversationTurn> ConversationHistory { get; init; } = Array.Empty<ConversationTurn>();
        public WorkflowHistory? PriorHistory { get; init; }
        public DetectedWorkflow? ActiveWorkflow { get; init; }
        public int ActiveStepIndex { get; init; }
    }
}
