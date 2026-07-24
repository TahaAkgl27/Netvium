namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Pre-LLM visual context extracted from the current capture via OCR and heuristics.
    /// This is the source of truth for what is on screen before vision analysis runs.
    /// </summary>
    public sealed class ScreenVisualContext
    {
        public string ActiveWindowTitle { get; init; } = string.Empty;
        public string OcrWindowTitle { get; init; } = string.Empty;
        public string MmcSnapIn { get; init; } = string.Empty;
        public string ConsoleType { get; init; } = string.Empty;
        public MmcConsoleKind MmcConsole { get; init; } = MmcConsoleKind.Unknown;
        public string SelectedTreeNode { get; init; } = string.Empty;
        public string SelectedListItem { get; init; } = string.Empty;
        public string OcrSelectedRow { get; init; } = string.Empty;
        public string VisibleDialogTitle { get; init; } = string.Empty;
        public string StatusBarText { get; init; } = string.Empty;
        public string OcrStatusBar { get; init; } = string.Empty;
        public string? DetectedEventId { get; init; }
        public string? EventDescriptionSummary { get; init; }
        public double OcrConfidence { get; init; }
        public string ApplicationFingerprint { get; init; } = string.Empty;
        public bool ApplicationChanged { get; init; }
        public IReadOnlyList<string> OcrLimitations { get; init; } = Array.Empty<string>();
        public bool HasModalDialog { get; init; }
        public string ModalTitle { get; init; } = string.Empty;
        public string ModalBodyText { get; init; } = string.Empty;
        public bool ModalPriority { get; init; }
        public string FocusedContext { get; init; } = string.Empty;
        public string OcrTreeText { get; init; } = string.Empty;
        public IReadOnlyList<NavigationTreeNode> NavigationTree { get; init; } = Array.Empty<NavigationTreeNode>();
        public string NavigationTreeSummary { get; init; } = string.Empty;
        public IReadOnlyList<DetectedVisibleControl> DetectedControls { get; init; } = Array.Empty<DetectedVisibleControl>();

        public bool HasLowOcrConfidence => OcrConfidence < 0.45;
    }
}
