namespace Netvium.Models.AIGuide
{
    public sealed class AdminToolGuidanceHint
    {
        public MmcConsoleKind VisibleTool { get; init; } = MmcConsoleKind.Unknown;
        public string VisibleToolName { get; init; } = string.Empty;
        public AdminUserIntent UserIntent { get; init; } = AdminUserIntent.Unknown;
        public MmcConsoleKind RecommendedTool { get; init; } = MmcConsoleKind.Unknown;
        public string RecommendedToolName { get; init; } = string.Empty;
        public bool IsToolMismatch { get; init; }
        public bool IsCorrectTool { get; init; }
        public bool UseLocalAnswer { get; init; }
        public string? LocalAnswer { get; init; }
        public string PromptDirective { get; init; } = string.Empty;
        public string ExpertModuleId { get; init; } = string.Empty;
        public string ExpertKnowledge { get; init; } = string.Empty;
        public bool HasModalPriority { get; init; }
        public string ModalTitle { get; init; } = string.Empty;
        public string ModalSummary { get; init; } = string.Empty;
        public string FocusedContext { get; init; } = string.Empty;
    }

    public sealed class AnswerQualityResult
    {
        public bool Passed { get; init; }
        public bool VisibleToolCorrect { get; init; }
        public bool IntentAddressed { get; init; }
        public bool WrongToolMentioned { get; init; }
        public bool Actionable { get; init; }
        public bool AvoidedStaleContext { get; init; } = true;
        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    }
}
