using Netvium.Models;

namespace Netvium.Models.AIGuide
{
    public sealed class AIGuideContextBuildInput
    {
        public ServerInfo? Server { get; init; }
        public WmiSnapshot? Snapshot { get; init; }
        public VisionObservation? Observation { get; init; }
        public ScreenCaptureSource CaptureSource { get; init; }
        public bool RdpSessionActive { get; init; }
        public DateTime AnalysisTimeUtc { get; init; } = DateTime.UtcNow;
    }
}
