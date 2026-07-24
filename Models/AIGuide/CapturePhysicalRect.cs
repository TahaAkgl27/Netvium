namespace Netvium.Models.AIGuide
{
    public readonly record struct CapturePhysicalRect(int X, int Y, int Width, int Height);

    public sealed class BlankDetectionResult
    {
        public bool IsBlank { get; init; }
        /// <summary>0–100; higher means more likely blank/unusable.</summary>
        public double BlankScore { get; init; }
        public string Reason { get; init; } = string.Empty;
        public double DarkPercent { get; init; }
        public double WhitePercent { get; init; }
        public double TransparentPercent { get; init; }
        public double VarianceScore { get; init; }
    }
}
