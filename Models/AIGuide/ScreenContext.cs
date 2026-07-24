namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// In-memory description of a captured screen. Image bytes never leave process memory in Sprint 1.
    /// </summary>
    public sealed class ScreenContext
    {
        public required string CaptureId { get; init; }
        public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
        public ScreenCaptureSource Source { get; init; }
        public int MonitorIndex { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[]? ImageBytes { get; init; }
        public string? ServerKey { get; init; }
        public string? ServerName { get; init; }
        public string? CaptureWarning { get; init; }
        public AIGuideFailureStage FailureStage { get; init; } = AIGuideFailureStage.None;
        public AIGuideCaptureDiagnostics? CaptureDiagnostics { get; init; }

        /// <summary>OCR and MMC detection from this capture only — never reused across captures.</summary>
        public ScreenVisualContext? VisualContext { get; set; }

        public bool HasImage => ImageBytes is { Length: > 0 };
    }
}
