namespace Netvium.Models.AIGuide
{
    public sealed class WorkflowState
    {
        public WorkflowStatus Status { get; init; } = WorkflowStatus.None;
        public DetectedWorkflow? Workflow { get; init; }
        public WorkflowProgress Progress { get; init; } = new();
        public double Confidence { get; init; }
        public string ConfidenceLabel { get; init; } = "Low";
        public string? PreviousApplication { get; init; }
        public string? CurrentApplication { get; init; }
        public bool ApplicationChanged { get; init; }
        public bool? WorkflowContinued { get; init; }
        public List<WorkflowEvidence> Evidence { get; init; } = new();
        public string? NextLikelyStepExplanation { get; init; }
        public WorkflowHistory History { get; init; } = new();
        public string StatusMessage { get; init; } = string.Empty;

        public static WorkflowState Empty() => new()
        {
            Status = WorkflowStatus.None,
            StatusMessage = "No workflow detected."
        };

        public static WorkflowState Unknown(string message) => new()
        {
            Status = WorkflowStatus.Unknown,
            StatusMessage = message,
            Confidence = 0,
            ConfidenceLabel = "Low"
        };
    }
}
