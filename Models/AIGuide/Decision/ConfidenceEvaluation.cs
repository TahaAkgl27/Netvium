using Netvium.Models.AIGuide.Decision;

namespace Netvium.Models.AIGuide.Decision
{
    public sealed class ConfidenceEvaluation
    {
        public int Score { get; init; }
        public string Label { get; init; } = "Medium";
        public IReadOnlyList<string> Factors { get; init; } = Array.Empty<string>();
    }

    public sealed class ConfidenceInput
    {
        public bool IsWrongScreen { get; init; }
        public bool IsUnknownScreen { get; init; }
        public bool ScreenCorrect { get; init; }
        public bool IntentDetected { get; init; }
        public bool WorkflowMatched { get; init; }
        public bool TargetVisible { get; init; }
        public bool TargetVerified { get; init; }
        public bool MissingRequiredTarget { get; init; }
        public bool IsGenericWorkflow { get; init; }
        public bool IsServerManagerFolderPermission { get; init; }
        public bool HallucinationGuardTriggered { get; init; }
        public bool WrongObjectDetected { get; init; }
        public bool ClarificationRequired { get; init; }
        public bool IntentUnknown { get; init; }
        public bool IsExploratoryQuestion { get; init; }
        public bool TargetSelectedButNotVerified { get; init; }
        public bool TargetVisibleAndSelectedAndIntentClear { get; init; }
        public bool IsFolderPermissionIntent { get; init; }
    }
}
