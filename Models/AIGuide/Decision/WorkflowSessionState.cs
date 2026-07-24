namespace Netvium.Models.AIGuide.Decision
{
    /// <summary>Persisted workflow goal across conversation turns (deterministic engine).</summary>
    public sealed class WorkflowSessionState
    {
        public AdminUserIntent Intent { get; init; }
        public WorkflowGoal Goal { get; init; }
        public WorkflowTarget Target { get; init; } = new();
        public string WorkflowName { get; init; } = string.Empty;
        public string LastStepId { get; init; } = string.Empty;
        public WorkflowScreenState LastState { get; init; }
        public string AnchorQuestion { get; init; } = string.Empty;
        public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

        public static WorkflowSessionState FromDecision(
            WorkflowDecisionResult decision,
            string anchorQuestion) =>
            new()
            {
                Intent = decision.Context.Intent,
                Goal = decision.Context.Goal,
                Target = decision.Context.Target,
                WorkflowName = decision.Context.WorkflowName,
                LastStepId = decision.NextStep.StepId,
                LastState = decision.Context.CurrentState,
                AnchorQuestion = anchorQuestion,
                UpdatedAtUtc = DateTime.UtcNow
            };
    }
}
