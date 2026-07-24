namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Result shape for a future vision API response. Not populated in Sprint 1.
    /// </summary>
    public sealed class VisionResult
    {
        public required string RequestId { get; init; }
        public bool Success { get; init; }
        public string? Explanation { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
    }
}
