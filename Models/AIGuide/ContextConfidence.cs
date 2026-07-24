namespace Netvium.Models.AIGuide
{
    public sealed class ContextConfidence
    {
        public double VisionConfidence { get; init; }
        public double ContextConfidenceScore { get; init; }
        public double OverallGuidanceConfidence { get; init; }
        public string OverallLabel { get; init; } = "Medium";
        public string VisionLabel { get; init; } = "Medium";
        public string ContextLabel { get; init; } = "Medium";

        public static string ToLabel(double score) =>
            score >= 0.75 ? "High" : score >= 0.45 ? "Medium" : "Low";
    }
}
