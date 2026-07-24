namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Application context from vision observation only — never inferred beyond visible evidence.
    /// </summary>
    public sealed class ApplicationContext
    {
        public string? DetectedApplication { get; init; }
        public string? WindowTitle { get; init; }
        public string? ScreenType { get; init; }
        public List<string> DetectedSoftware { get; init; } = new();
        public double VisionConfidence { get; init; }
        public bool IsAvailable { get; init; }
    }
}
