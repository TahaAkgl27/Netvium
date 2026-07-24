namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Fingerprint of a captured screen used to detect navigation between apps/windows.
    /// </summary>
    public sealed class ScreenFingerprint
    {
        public string PerceptualHash { get; init; } = string.Empty;
        public string ActiveWindowTitle { get; init; } = string.Empty;
        public string DetectedApplication { get; init; } = string.Empty;
        public string ApplicationFingerprint { get; init; } = string.Empty;
        public string OcrKeywords { get; init; } = string.Empty;
        public double ImageDifferenceScore { get; init; }
    }

    public sealed class ScreenChangeResult
    {
        public bool IsNewScreen { get; init; }
        public string Reason { get; init; } = string.Empty;
        public int HashHammingDistance { get; init; }
        public double ImageDifferenceScore { get; init; }
    }
}
