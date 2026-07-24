namespace Netvium.AIGuide.Tests;

public sealed class Sprint2GuardTestDocument
{
    public List<ScreenCompatibilityTestCase> ScreenCompatibility { get; init; } = [];
    public List<ConfidenceTestCase> Confidence { get; init; } = [];
    public List<DecideTestCase> Decide { get; init; } = [];
    public List<HallucinationTestCase> Hallucination { get; init; } = [];
}

public sealed class ScreenCompatibilityTestCase
{
    public string Name { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public string ScreenKind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool ExpectCompatible { get; init; }
    public bool? ExpectConsumerApp { get; init; }
}

public sealed class ConfidenceTestCase
{
    public string Name { get; init; } = string.Empty;
    public bool IsWrongScreen { get; init; }
    public bool ScreenCorrect { get; init; }
    public bool IntentDetected { get; init; }
    public bool WorkflowMatched { get; init; }
    public bool TargetVisible { get; init; }
    public bool TargetVerified { get; init; }
    public bool HallucinationGuardTriggered { get; init; }
    public bool IsFolderPermissionIntent { get; init; }
    public string ExpectLabel { get; init; } = string.Empty;
    public int? ExpectScore { get; init; }
}

public sealed class DecideTestCase
{
    public string Name { get; init; } = string.Empty;
    public string UserQuestion { get; init; } = string.Empty;
    public string ActiveWindowTitle { get; init; } = string.Empty;
    public string ScreenKind { get; init; } = string.Empty;
    public string? OcrStatusBar { get; init; }
    public string? StatusBarText { get; init; }
    public string? SelectedListItem { get; init; }
    public string ExpectWorkflowName { get; init; } = string.Empty;
    public bool? ExpectShouldRefuse { get; init; }
    public string? ExpectConfidence { get; init; }
    public int? ExpectMinManualSteps { get; init; }
    public string? ExpectNextStepContains { get; init; }
    public string? ExpectObservationContains { get; init; }
    public string? ExpectIntent { get; init; }
    public bool? ExpectShouldAskQuestion { get; init; }
}

public sealed class HallucinationTestCase
{
    public string Name { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public bool TargetVisible { get; init; }
    public string Instruction { get; init; } = string.Empty;
    public bool ExpectPassed { get; init; }
    public string? ExpectSanitizedContains { get; init; }
}
