namespace Netvium.Models.AIGuide
{
    public enum AIGuideFailureStage
    {
        None,
        CaptureTargetResolve,
        Capture,
        BlankCapture,
        VisionRequest,
        VisionTimeout,
        VisionApiError,
        VisionJsonParse,
        ContextBuild,
        SmartSummary,
        WorkflowDetection,
        MentorBuild,
        Cancelled,
        Unknown
    }
}
