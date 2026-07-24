namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Combined runtime context for AI Guide guidance — vision + telemetry + server metadata.
    /// </summary>
    public sealed class RuntimeContext
    {
        public ServerContext Server { get; init; } = new();
        public ApplicationContext Application { get; init; } = new();
        public DetectedServerRole DetectedRole { get; init; } = new();
        public LiveMetricsContext Metrics { get; init; } = new();
        public DateTime AnalysisTimeUtc { get; init; } = DateTime.UtcNow;
        public ContextConfidence Confidence { get; init; } = new();
    }
}
