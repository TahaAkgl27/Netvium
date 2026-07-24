namespace Netvium.Models.AIGuide.Decision
{
    public enum WorkflowGoal
    {
        Unknown,
        GrantPermission,
        TroubleshootPermission,
        AuditPermission,
        ResetPassword,
        UnlockAccount,
        CreateUser,
        DeleteUser,
        ManageGroupMembership,
        CreateReservation,
        CreateDnsRecord,
        RestartService,
        ReviewEvents,
        ConfigureIis,
        GeneralGuidance
    }

    public enum WorkflowScreenState
    {
        Unknown,
        WrongToolOpen,
        WaitingForFolder,
        TargetNotVisible,
        TargetVisible,
        SecurityTabOpen,
        EditDialogOpen,
        PermissionSelected,
        ApplyPending,
        Completed,
        ClarificationPending
    }

    public sealed class EvidenceItem
    {
        public required string Category { get; init; }
        public required string Statement { get; init; }
        public bool IsConfirmed { get; init; } = true;
    }

    public sealed class EvidenceBundle
    {
        public IReadOnlyList<EvidenceItem> Visual { get; init; } = Array.Empty<EvidenceItem>();
        public IReadOnlyList<EvidenceItem> Conversation { get; init; } = Array.Empty<EvidenceItem>();
        public IReadOnlyList<EvidenceItem> Knowledge { get; init; } = Array.Empty<EvidenceItem>();
        public IReadOnlyList<EvidenceItem> Workflow { get; init; } = Array.Empty<EvidenceItem>();

        public int TotalCount => Visual.Count + Conversation.Count + Knowledge.Count + Workflow.Count;

        public IEnumerable<EvidenceItem> All =>
            Visual.Concat(Conversation).Concat(Knowledge).Concat(Workflow);
    }

    public sealed class AssumptionAnalysis
    {
        public IReadOnlyList<string> Known { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Assumed { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Unknown { get; init; } = Array.Empty<string>();
        public int AssumptionCount => Assumed.Count;
    }

    public sealed class ContradictionResult
    {
        public bool HasContradiction { get; init; }
        public IReadOnlyList<string> Contradictions { get; init; } = Array.Empty<string>();
    }

    public sealed class SelfVerificationResult
    {
        public bool Passed { get; init; }
        public bool WrongScreen { get; init; }
        public bool ActionOnHiddenTarget { get; init; }
        public bool WorkflowViolation { get; init; }
        public bool HasContradiction { get; init; }
        public bool MissingInformation { get; init; }
        public string? CorrectedInstruction { get; init; }
        public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    }

    public sealed class EvidenceCoverageBreakdown
    {
        public int Visual { get; init; }
        public int Conversation { get; init; }
        public int Knowledge { get; init; }
        public int Workflow { get; init; }
        public int Total => Visual + Conversation + Knowledge + Workflow;
    }

    public sealed class ReliabilityEvaluation
    {
        public int Score { get; init; }
        public string Label { get; init; } = "Medium";
        public EvidenceCoverageBreakdown Coverage { get; init; } = new();
        public int WorkflowMatch { get; init; }
        public int GoalMatch { get; init; }
        public int VisualConfirmation { get; init; }
        public int ContradictionCheck { get; init; }
        public int AssumptionPenalty { get; init; }
        public IReadOnlyList<string> Factors { get; init; } = Array.Empty<string>();
    }

    public sealed class ReasoningDebugInfo
    {
        public string ScreenType { get; init; } = string.Empty;
        public string Intent { get; init; } = string.Empty;
        public string Goal { get; init; } = string.Empty;
        public string Workflow { get; init; } = string.Empty;
        public string CurrentState { get; init; } = string.Empty;
        public string NextState { get; init; } = string.Empty;
        public int EvidenceCount { get; init; }
        public EvidenceCoverageBreakdown EvidenceCoverage { get; init; } = new();
        public int AssumptionCount { get; init; }
        public int ReliabilityScore { get; init; }
        public string ReliabilityLabel { get; init; } = string.Empty;
        public string ConfidenceLabel { get; init; } = string.Empty;
        public string ReasonForDecision { get; init; } = string.Empty;
        public bool VerificationPassed { get; init; }
        public IReadOnlyList<string> VerificationIssues { get; init; } = Array.Empty<string>();
    }
}
