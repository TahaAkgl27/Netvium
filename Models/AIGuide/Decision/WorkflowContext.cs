namespace Netvium.Models.AIGuide.Decision
{
    /// <summary>
    /// Authoritative decision payload produced by WorkflowDecisionEngine.
    /// LLM must not answer without this context.
    /// </summary>
    public sealed class WorkflowContext
    {
        public MmcConsoleKind ScreenType { get; init; } = MmcConsoleKind.Unknown;
        public string ScreenDisplayName { get; init; } = string.Empty;
        public AdminUserIntent Intent { get; init; } = AdminUserIntent.Unknown;
        public WorkflowGoal Goal { get; init; } = WorkflowGoal.Unknown;
        public WorkflowTarget Target { get; init; } = new();
        public string CurrentStep { get; init; } = string.Empty;
        public WorkflowScreenState CurrentState { get; init; } = WorkflowScreenState.Unknown;
        public WorkflowScreenState NextState { get; init; } = WorkflowScreenState.Unknown;
        public int CurrentStepIndex { get; init; }
        public int TotalSteps { get; init; }
        public bool TargetVisible { get; init; }
        public string WorkflowName { get; init; } = string.Empty;
        public string ConfidenceCandidate { get; init; } = "Medium";
        public bool UseTurkish { get; init; }
        public string ScreenObservation { get; init; } = string.Empty;
        public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
        public EvidenceBundle? StructuredEvidence { get; init; }
        public AssumptionAnalysis? Assumptions { get; init; }
        public IReadOnlyList<string> VisibleObjects { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AllowedScreens { get; init; } = Array.Empty<string>();
        public int ConfidenceScore { get; init; }
        public int ReliabilityScore { get; init; }
        public string ReliabilityLabel { get; init; } = "Medium";
        public ReasoningDebugInfo? DebugInfo { get; init; }
        public bool IsWrongScreen { get; init; }
        public bool IsConsumerApp { get; init; }
        public bool MissingRequiredTarget { get; init; }
        public bool ShouldRefuse { get; init; }
        public bool ShouldContinue { get; init; } = true;
        public bool ShouldAskQuestion { get; init; }
        public bool IsClarification { get; init; }
        public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
        public string ClarificationQuestion { get; init; } = string.Empty;
        public bool WrongObjectDetected { get; init; }
    }
}
