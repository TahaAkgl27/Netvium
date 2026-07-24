namespace Netvium.Models.AIGuide.Decision
{
    public sealed class WorkflowStepResult
    {
        public string StepId { get; init; } = string.Empty;
        public int StepIndex { get; init; }
        public string Instruction { get; init; } = string.Empty;
        public bool IsComplete { get; init; }
        public string? CompletionNote { get; init; }
    }
}
