namespace Netvium.Models.AIGuide.Decision
{
    public sealed class WorkflowDecisionInput
    {
        public required string UserQuestion { get; init; }
        public ScreenVisualContext? VisualContext { get; init; }
        public VisionObservation? Observation { get; init; }
        public IReadOnlyList<ConversationTurn> PriorTurns { get; init; } = Array.Empty<ConversationTurn>();
        public WorkflowSessionState? SessionState { get; init; }

        /// <summary>Anchor question from prior turns; empty means use <see cref="UserQuestion"/>.</summary>
        public string EffectiveQuestion { get; init; } = string.Empty;

        public string ResolvedQuestion =>
            string.IsNullOrWhiteSpace(EffectiveQuestion) ? UserQuestion : EffectiveQuestion;
    }
}
