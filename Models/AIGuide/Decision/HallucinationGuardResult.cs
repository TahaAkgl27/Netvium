namespace Netvium.Models.AIGuide.Decision
{
    public sealed class HallucinationGuardResult
    {
        public bool Passed { get; init; }
        public string SanitizedInstruction { get; init; } = string.Empty;
        public IReadOnlyList<string> Violations { get; init; } = Array.Empty<string>();
    }
}
