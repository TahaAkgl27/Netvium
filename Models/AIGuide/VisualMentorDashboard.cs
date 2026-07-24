namespace Netvium.Models.AIGuide
{
    public sealed class MentorWarning
    {
        public required string Message { get; init; }
        public ObservationSeverity Severity { get; init; } = ObservationSeverity.Warning;
    }

    public sealed class MentorEvidenceItem
    {
        public required string Label { get; init; }
        public required string Detail { get; init; }
    }

    public sealed class CurrentScreenSummary
    {
        public string WindowTitle { get; init; } = string.Empty;
        public string Application { get; init; } = string.Empty;
        public List<string> SelectedObjects { get; init; } = new();
        public List<string> Dialogs { get; init; } = new();
        public List<string> VisibleWarnings { get; init; } = new();
        public List<string> VisibleErrors { get; init; } = new();
        public string Narrative { get; init; } = string.Empty;
    }

    public sealed class ScreenChangeInfo
    {
        public bool ApplicationChanged { get; init; }
        public bool WindowTitleChanged { get; init; }
        public bool DialogOpened { get; init; }
        public bool DialogClosed { get; init; }
        public bool SelectionChanged { get; init; }
        public bool? WorkflowContinued { get; init; }
        public string? PreviousApplication { get; init; }
        public string? CurrentApplication { get; init; }
    }

    public sealed class VisualMentorDashboard
    {
        public bool IsReady { get; init; }
        public string StatusBadge { get; init; } = "Screen Ready";
        public string? ServerName { get; init; }
        public string? Role { get; init; }
        public string? Application { get; init; }
        public string? WorkflowName { get; init; }
        public string ConfidenceLabel { get; init; } = "Medium";
        public double ConfidenceScore { get; init; }

        public CurrentScreenSummary CurrentScreen { get; init; } = new();
        public ObservationResult Observations { get; init; } = new();
        public WorkflowState? Workflow { get; init; }
        public List<string> WorkflowTimelineSteps { get; init; } = new();
        public List<QuestionSuggestion> SuggestedQuestions { get; init; } = new();
        public LearningSuggestion LearningTopics { get; init; } = new();
        public List<MentorWarning> Warnings { get; init; } = new();
        public List<MentorEvidenceItem> Evidence { get; init; } = new();
        public ObservationTimeline Timeline { get; init; } = new();
        public ScreenChangeInfo ScreenChange { get; init; } = new();
        public string Reminder { get; init; } = "AI Guide explains the visible screen only. It never controls your computer.";
    }
}
