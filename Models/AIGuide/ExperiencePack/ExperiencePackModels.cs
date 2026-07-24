using System.Text.Json.Serialization;
using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.KnowledgePack;

namespace Netvium.Models.AIGuide.ExperiencePack
{
    public sealed class ExperiencePackBundle
    {
        public ExperiencePackDocument WindowsAdmin { get; init; } = new();
        public ExperiencePackDocument ActiveDirectory { get; init; } = new();
        public ExperiencePackDocument Dhcp { get; init; } = new();
        public ExperiencePackDocument Dns { get; init; } = new();
        public ExperiencePackDocument Iis { get; init; } = new();
        public ExperiencePackDocument FilePermission { get; init; } = new();
        public ExperiencePackDocument EventViewer { get; init; } = new();

        public IEnumerable<ExperiencePackDocument> AllDocuments =>
        [
            WindowsAdmin,
            ActiveDirectory,
            Dhcp,
            Dns,
            Iis,
            FilePermission,
            EventViewer
        ];
    }

    public sealed class ExperiencePackDocument
    {
        public int Version { get; init; } = 1;

        [JsonPropertyName("domain")]
        public string Domain { get; init; } = string.Empty;

        public List<ExperienceEntry> Entries { get; init; } = [];
    }

    public sealed class ExperienceEntry
    {
        public string Id { get; init; } = string.Empty;
        public int Priority { get; init; }
        public ExperienceMatch Match { get; init; } = new();
        public List<LocalizedText> CommonMistakes { get; init; } = [];
        public List<LocalizedText> LikelyCauses { get; init; } = [];
        public List<LocalizedText> RiskWarnings { get; init; } = [];
        public List<LocalizedText> VerificationChecks { get; init; } = [];
        public List<LocalizedText> TroubleshootingSteps { get; init; } = [];
        public List<LocalizedText> ExpertTips { get; init; } = [];
        public List<LocalizedText> WhenToAskClarification { get; init; } = [];
        public List<LocalizedText> WhenToEscalate { get; init; } = [];
    }

    public sealed class ExperienceMatch
    {
        public List<string> Intents { get; init; } = [];
        public List<string> Goals { get; init; } = [];
        public List<string> Workflows { get; init; } = [];
        public List<string> Screens { get; init; } = [];
        public List<string> States { get; init; } = [];
        public List<string> EventIds { get; init; } = [];
        public List<string> QuestionKeywords { get; init; } = [];
    }

    public sealed class ExperienceHint
    {
        public required string Id { get; init; }
        public required string Category { get; init; }
        public required string Text { get; init; }
        public int Priority { get; init; }
    }

    public sealed class ExperienceSelectionInput
    {
        public AdminUserIntent Intent { get; init; }
        public WorkflowGoal Goal { get; init; }
        public string WorkflowName { get; init; } = string.Empty;
        public MmcConsoleKind ScreenType { get; init; }
        public WorkflowScreenState CurrentState { get; init; }
        public string UserQuestion { get; init; } = string.Empty;
        public string? DetectedEventId { get; init; }
        public bool UseTurkish { get; init; }
        public bool IsClarification { get; init; }
        public bool IsWrongScreen { get; init; }
    }
}
