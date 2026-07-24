namespace Netvium.Models.AIGuide.Decision
{
    public sealed class ScreenIntentCompatibilityResult
    {
        public bool IsCompatible { get; init; }
        public bool IsConsumerApp { get; init; }
        public IReadOnlyList<string> AllowedScreenLabels { get; init; } = Array.Empty<string>();
        public string Reason { get; init; } = string.Empty;
    }
}
