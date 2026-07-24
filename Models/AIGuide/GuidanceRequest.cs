using System.Text.Json.Serialization;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Models.AIGuide
{
    public sealed class GuidanceRequest
    {
        public required string RequestId { get; init; }
        public required string UserQuestion { get; init; }
        public required VisionObservation Observation { get; init; }
        public string? ObservationJson { get; init; }
        public ScreenCaptureSource CaptureSource { get; init; }
        public string? ServerName { get; init; }
        public string? ServerKey { get; init; }
        public DateTime AnalyzedAtUtc { get; init; }
        public RuntimeContext? RuntimeContext { get; init; }
        public WorkflowState? WorkflowState { get; init; }
        public IReadOnlyList<ConversationTurn> PriorTurns { get; init; } = Array.Empty<ConversationTurn>();
        public ScreenVisualContext? VisualContext { get; init; }
        public bool IsFreshVisualContext { get; init; }

        /// <summary>Current full-resolution PNG, attached to the multimodal guidance request only.</summary>
        [JsonIgnore]
        public byte[]? ScreenshotImageBytes { get; init; }
        public int ScreenshotWidth { get; init; }
        public int ScreenshotHeight { get; init; }
        public bool HasScreenshot => ScreenshotImageBytes is { Length: > 0 };
        public string GuideMode { get; init; } = "ObserveOnly";

        public AdminToolGuidanceHint? ToolGuidance { get; init; }
        public bool IncludeRuntimeContext { get; init; }
        public bool SuppressManualControlNote { get; init; }
        public string? ExpertModuleId { get; init; }
        public string? ExpertKnowledge { get; init; }
        public NavigationGuidanceHint? NavigationGuidance { get; init; }
        public WorkflowContext? WorkflowContext { get; init; }
        public WorkflowDecisionResult? WorkflowDecision { get; init; }
    }
}
