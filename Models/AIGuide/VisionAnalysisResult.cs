namespace Netvium.Models.AIGuide
{
    public sealed class VisionAnalysisResult
    {
        public required string RequestId { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? SafeDetail { get; init; }
        public VisionObservation? Observation { get; init; }
        public string? RawJson { get; init; }
        public ScreenCaptureSource CaptureSource { get; init; }
        public DateTime AnalyzedAtUtc { get; init; } = DateTime.UtcNow;
        public int ImageBytesLength { get; init; }
        public AIGuideFailureStage FailureStage { get; init; } = AIGuideFailureStage.Unknown;
        public int? VisionStatusCode { get; init; }

        public static VisionAnalysisResult Failed(
            string requestId,
            ScreenCaptureSource source,
            string message,
            int imageBytesLength = 0,
            AIGuideFailureStage stage = AIGuideFailureStage.Unknown,
            int? visionStatusCode = null,
            string? safeDetail = null) =>
            new()
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = message,
                SafeDetail = safeDetail,
                CaptureSource = source,
                ImageBytesLength = imageBytesLength,
                FailureStage = stage,
                VisionStatusCode = visionStatusCode
            };

        public static VisionAnalysisResult Disabled(string requestId, ScreenCaptureSource source) =>
            Failed(
                requestId,
                source,
                "AI Guide vision is disabled in settings.",
                stage: AIGuideFailureStage.VisionRequest,
                safeDetail: "Enable AI Guide Vision in Settings to analyze screenshots.");
    }
}
