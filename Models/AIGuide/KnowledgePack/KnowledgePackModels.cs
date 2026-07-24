using System.Text.Json.Serialization;

namespace Netvium.Models.AIGuide.KnowledgePack
{
    public sealed class KnowledgePackBundle
    {
        public IntentPatternsDocument IntentPatterns { get; init; } = new();
        public ScreenCompatibilityDocument ScreenCompatibility { get; init; } = new();
        public WorkflowDefinitionsDocument WorkflowDefinitions { get; init; } = new();
        public ResponseTemplatesDocument ResponseTemplates { get; init; } = new();
        public ConfidenceRulesDocument ConfidenceRules { get; init; } = new();
        public HallucinationRulesDocument HallucinationRules { get; init; } = new();
        public ContradictionRulesDocument ContradictionRules { get; init; } = new();
    }

    public sealed class IntentPatternsDocument
    {
        public int Version { get; init; } = 1;
        public List<IntentPatternRule> Rules { get; init; } = [];
        public FolderPermissionExtractionRules FolderPermissionExtraction { get; init; } = new();
    }

    public sealed class IntentPatternRule
    {
        public string Intent { get; init; } = string.Empty;
        public int Priority { get; init; } = 100;
        public List<string> Keywords { get; init; } = [];
        public List<string> AnyOf { get; init; } = [];
        public string? VisualContextField { get; init; }
        public string? VisualContextValue { get; init; }
    }

    public sealed class FolderPermissionExtractionRules
    {
        public List<string> PermissionKeywords { get; init; } = [];
        public List<string> Blockers { get; init; } = [];
        public List<string> ContextKeywords { get; init; } = [];
        public List<string> UsernameExclusions { get; init; } = [];
        public List<PermissionLevelRule> PermissionLevels { get; init; } = [];
    }

    public sealed class PermissionLevelRule
    {
        public string Id { get; init; } = string.Empty;
        public List<string> Keywords { get; init; } = [];
        public LocalizedText Label { get; init; } = new();
    }

    public sealed class ScreenCompatibilityDocument
    {
        public int Version { get; init; } = 1;
        public List<ConsumerAppRule> ConsumerApps { get; init; } = [];
        public List<IntentScreenRule> IntentScreens { get; init; } = [];
        public WrongScreenRules WrongScreen { get; init; } = new();
    }

    public sealed class ConsumerAppRule
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public List<string> Patterns { get; init; } = [];
    }

    public sealed class IntentScreenRule
    {
        public string Intent { get; init; } = string.Empty;
        public List<string> AllowedScreens { get; init; } = [];
        public List<string> AllowedScreenLabels { get; init; } = [];
    }

    public sealed class WrongScreenRules
    {
        public Dictionary<string, LocalizedText> Observations { get; init; } = new();
        public Dictionary<string, List<RedirectStepRule>> RedirectSteps { get; init; } = new();
    }

    public sealed class RedirectStepRule
    {
        [JsonPropertyName("en")]
        public string En { get; init; } = string.Empty;

        [JsonPropertyName("tr")]
        public string Tr { get; init; } = string.Empty;

        [JsonPropertyName("requiresToken")]
        public string? RequiresToken { get; init; }

        [JsonPropertyName("fallbackWhenNoPath")]
        public bool FallbackWhenNoPath { get; init; }

        public LocalizedText AsLocalized() => new() { En = En, Tr = Tr };
    }

    public sealed class WorkflowDefinitionsDocument
    {
        public int Version { get; init; } = 1;
        public List<WorkflowDefinition> Workflows { get; init; } = [];
        public ClarificationDefinitions Clarification { get; init; } = new();
        public WrongObjectRules WrongObject { get; init; } = new();
    }

    public sealed class WorkflowDefinition
    {
        public string Id { get; init; } = string.Empty;
        public List<string> Intents { get; init; } = [];
        public int DefaultTotalSteps { get; init; } = 1;
        public Dictionary<string, int> TotalStepsByScreen { get; init; } = new();
        public Dictionary<string, LocalizedText> Observations { get; init; } = new();
        public List<WorkflowStepDefinition> Steps { get; init; } = [];
        public LocalizedText GenericStep { get; init; } = new();
    }

    public sealed class WorkflowStepDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Condition { get; init; } = string.Empty;
        public LocalizedText Instruction { get; init; } = new();
    }

    public sealed class ClarificationDefinitions
    {
        public Dictionary<string, ClarificationIntentRules> ByIntent { get; init; } = new();
    }

    public sealed class ClarificationIntentRules
    {
        public List<string> RequiredFields { get; init; } = [];
        public Dictionary<string, LocalizedText> FieldLabels { get; init; } = new();
        public Dictionary<string, LocalizedText> Questions { get; init; } = new();
        public Dictionary<string, List<string>> TriggerKeywords { get; init; } = new();
        public List<string> SkipWhenKeywords { get; init; } = [];
    }

    public sealed class WrongObjectRules
    {
        public LocalizedText Message { get; init; } = new();
        public List<string> FileExtensions { get; init; } = [];
    }

    public sealed class ResponseTemplatesDocument
    {
        public int Version { get; init; } = 1;
        public Dictionary<string, LocalizedText> Sections { get; init; } = new();
        public Dictionary<string, LocalizedText> IntentLabels { get; init; } = new();
        public Dictionary<string, LocalizedText> GoalTemplates { get; init; } = new();
        public LocalizedText WrongScreenTemplate { get; init; } = new();
        public LocalizedText StructuredTemplate { get; init; } = new();
        public LocalizedText ClarificationTemplate { get; init; } = new();
    }

    public sealed class ConfidenceRulesDocument
    {
        public int Version { get; init; } = 1;
        public Dictionary<string, int> Scoring { get; init; } = new();
        public Dictionary<string, int> Thresholds { get; init; } = new();
        public Dictionary<string, string> Overrides { get; init; } = new();
    }

    public sealed class HallucinationRulesDocument
    {
        public int Version { get; init; } = 1;
        public List<string> ActionKeywords { get; init; } = [];
        public List<string> NavigateKeywords { get; init; } = [];
        public LocalizedText SafeNavigateWithPath { get; init; } = new();
        public LocalizedText SafeNavigateGeneric { get; init; } = new();
        public List<string> Rules { get; init; } = [];
    }

    public sealed class LocalizedText
    {
        [JsonPropertyName("en")]
        public string En { get; init; } = string.Empty;

        [JsonPropertyName("tr")]
        public string Tr { get; init; } = string.Empty;

        public string Get(bool useTurkish) => useTurkish
            ? (string.IsNullOrWhiteSpace(Tr) ? En : Tr)
            : En;

        public string Format(bool useTurkish, IReadOnlyDictionary<string, string> tokens)
        {
            var text = Get(useTurkish);
            foreach (var (key, value) in tokens)
                text = text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
            return text;
        }
    }

    public sealed class ContradictionRulesDocument
    {
        public int Version { get; init; } = 1;
        public List<string> NotVisiblePhrases { get; init; } = [];
        public List<string> ActionOnTargetPhrases { get; init; } = [];
        public List<string> NavigateSafePhrases { get; init; } = [];
        public List<ContradictionRuleDefinition> Rules { get; init; } = [];
    }

    public sealed class ContradictionRuleDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool RequiresTargetNotVisible { get; init; }
        public bool RequiresActionOnTarget { get; init; }
        public bool RequiresFolderInInstruction { get; init; }
        public bool ForbidUnlessNavigatePhrase { get; init; }
        public bool RequiresWrongScreen { get; init; }
        public bool RequiresClarificationPending { get; init; }
        public bool RequiresWorkflowStepBeyondClarify { get; init; }
    }
}
