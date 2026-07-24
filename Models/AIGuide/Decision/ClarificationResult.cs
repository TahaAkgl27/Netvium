namespace Netvium.Models.AIGuide.Decision
{
    public sealed class ClarificationResult
    {
        public bool Required { get; init; }
        public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
        public string Question { get; init; } = string.Empty;
        public string ConfidenceLabel { get; init; } = "Low";
    }
}
