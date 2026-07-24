using Netvium.Models.AIGuide.ExperiencePack;

namespace Netvium.Models.AIGuide.Decision
{
    public sealed class WorkflowDecisionResult
    {
        public WorkflowContext Context { get; init; } = new();
        public WorkflowStepResult NextStep { get; init; } = new();
        public bool HasWorkflow { get; init; }
        public bool UseLocalResponse { get; init; } = true;
        public string StructuredSummary { get; init; } = string.Empty;
        public bool ShouldRefuse { get; init; }
        public bool ShouldContinue { get; init; } = true;
        public bool ShouldAskQuestion { get; init; }
        public IReadOnlyList<string> ManualStepInstructions { get; init; } = Array.Empty<string>();
        public EvidenceBundle? Evidence { get; init; }
        public ReliabilityEvaluation? Reliability { get; init; }
        public SelfVerificationResult? Verification { get; init; }
        public ReasoningDebugInfo? DebugInfo { get; init; }
        public IReadOnlyList<ExperienceHint> ExperienceHints { get; init; } = Array.Empty<ExperienceHint>();
    }
}
