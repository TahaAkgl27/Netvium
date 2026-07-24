namespace Netvium.Models.AIGuide
{
    public sealed class AIGuideFailureInfo
    {
        public AIGuideFailureStage Stage { get; init; } = AIGuideFailureStage.Unknown;
        public string UserMessage { get; init; } = string.Empty;
        public string SafeDetail { get; init; } = string.Empty;
        public string TechnicalMessage { get; init; } = string.Empty;
        public ScreenCaptureSource CaptureSource { get; init; }
        public int CaptureWidth { get; init; }
        public int CaptureHeight { get; init; }
        public int CaptureBytes { get; init; }
        public int? VisionStatusCode { get; init; }
        public long ElapsedMs { get; init; }
        public long AnalysisId { get; init; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public string? ExceptionType { get; init; }
        public string? ExceptionMessage { get; init; }
        public string? OriginMethod { get; init; }

        public static string RetryHint => Services.AIGuide.AIGuideLanguage.RetryHint;

        public static string OpenHint => Services.AIGuide.AIGuideLanguage.OpenHint;

        public static AIGuideFailureInfo Create(
            AIGuideFailureStage stage,
            string userMessage,
            string safeDetail,
            string? technicalMessage = null,
            long analysisId = 0,
            long elapsedMs = 0,
            ScreenCaptureSource captureSource = ScreenCaptureSource.Unknown,
            int captureWidth = 0,
            int captureHeight = 0,
            int captureBytes = 0,
            int? visionStatusCode = null,
            string? exceptionType = null,
            string? exceptionMessage = null,
            string? originMethod = null) =>
            new()
            {
                Stage = stage,
                UserMessage = userMessage,
                SafeDetail = safeDetail,
                TechnicalMessage = technicalMessage ?? safeDetail,
                AnalysisId = analysisId,
                ElapsedMs = elapsedMs,
                CaptureSource = captureSource,
                CaptureWidth = captureWidth,
                CaptureHeight = captureHeight,
                CaptureBytes = captureBytes,
                VisionStatusCode = visionStatusCode,
                ExceptionType = exceptionType,
                ExceptionMessage = exceptionMessage,
                OriginMethod = originMethod
            };
    }
}
