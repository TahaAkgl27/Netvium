using Netvium.Models.AIGuide;

namespace AIGuideQaRunner;

internal sealed class QaScenario
{
    public int TestNo { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Screen { get; init; } = string.Empty;
    public string UserQuestion { get; init; } = string.Empty;
    public QaExpected Expected { get; init; } = new();
    public QaContext Context { get; init; } = new();
}

internal sealed class QaExpected
{
    public string Intent { get; init; } = string.Empty;
    public string Workflow { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public bool ShouldAskQuestion { get; init; }
    public bool ShouldRefuse { get; init; }
    public bool ShouldContinue { get; init; }
    public bool ShouldDetectWrongScreen { get; init; }
    public string Result { get; init; } = string.Empty;
}

internal sealed class QaContext
{
    public string ScreenText { get; init; } = string.Empty;
    public MmcConsoleKind ScreenKind { get; init; } = MmcConsoleKind.Unknown;
    public string SelectedListItem { get; init; } = string.Empty;
    public string SelectedTreeNode { get; init; } = string.Empty;
    public string AddressOrPathVisible { get; init; } = string.Empty;
    public string? PriorUserMessage { get; init; }
    public IReadOnlyList<string> PriorTurns { get; init; } = Array.Empty<string>();
}

internal sealed class QaActual
{
    public string DetectedScreen { get; init; } = string.Empty;
    public string DetectedIntent { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string GuidanceSnippet { get; init; } = string.Empty;
    public bool WrongScreenDetected { get; init; }
    public bool AsksQuestion { get; init; }
    public bool RefusesWrongTool { get; init; }
    public bool MentionsHiddenTarget { get; init; }
    public bool UsesWrongSelection { get; init; }
    public bool HasVisualEvidence { get; init; }
    public bool HallucinationRisk { get; init; }
    public bool HasExperienceSection { get; init; }
    public string ExperienceHintText { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

internal sealed class QaScenarioFile
{
    public int Version { get; init; }
    public int ScenarioCount { get; init; }
    public List<QaScenarioDto> Scenarios { get; init; } = [];
}

internal sealed class QaScenarioDto
{
    public int TestNo { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Screen { get; init; } = string.Empty;
    public string UserQuestion { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string ExpectedWorkflow { get; init; } = string.Empty;
    public string ExpectedConfidence { get; init; } = string.Empty;
    public bool ShouldAskQuestion { get; init; }
    public bool ShouldRefuse { get; init; }
    public bool ShouldContinue { get; init; }
    public bool ShouldDetectWrongScreen { get; init; }
    public string ExpectedResult { get; init; } = string.Empty;
    public QaContextDto Context { get; init; } = new();
}

internal sealed class QaContextDto
{
    public string ScreenText { get; init; } = string.Empty;
    public string ScreenKind { get; init; } = "Unknown";
    public string SelectedListItem { get; init; } = string.Empty;
    public string SelectedTreeNode { get; init; } = string.Empty;
    public string AddressOrPathVisible { get; init; } = string.Empty;
    public string? PriorUserMessage { get; init; }
    public List<string> PriorTurns { get; init; } = [];
}
