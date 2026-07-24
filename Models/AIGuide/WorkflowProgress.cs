namespace Netvium.Models.AIGuide
{
    public sealed class WorkflowProgress
    {
        public int CurrentStepIndex { get; init; }
        public int TotalSteps { get; init; }
        public string CurrentStepName { get; init; } = string.Empty;
        public string? NextLikelyStepHint { get; init; }
        public double ProgressPercent { get; init; }

        public string DisplayProgress => TotalSteps > 0
            ? $"{CurrentStepIndex} of {TotalSteps}"
            : "—";
    }
}
