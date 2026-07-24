using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class AIGuideExceptionMapper
    {
        public static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException agg && agg.InnerException != null)
                ex = agg.InnerException;
            return ex;
        }

        public static AIGuideFailureInfo Map(
            Exception ex,
            string pipelineCheckpoint,
            long analysisId,
            long elapsedMs,
            ScreenContext? screen = null,
            VisionAnalysisResult? vision = null)
        {
            var root = Unwrap(ex);
            var typeName = root.GetType().FullName ?? root.GetType().Name;
            var message = root.Message ?? string.Empty;

            AIGuideAnalysisLogger.PipelineException(
                pipelineCheckpoint,
                analysisId,
                typeName,
                message,
                root.StackTrace);

            var stage = ResolveStage(root, pipelineCheckpoint, message, screen, vision);
            var userMessage = ResolveUserMessage(stage, root, message);
            var safeDetail = ResolveSafeDetail(stage, root, message);

            return AIGuideFailureInfo.Create(
                stage,
                userMessage,
                safeDetail,
                technicalMessage: BuildTechnicalMessage(root, pipelineCheckpoint),
                analysisId: analysisId,
                elapsedMs: elapsedMs,
                captureSource: screen?.Source ?? vision?.CaptureSource ?? ScreenCaptureSource.Unknown,
                captureWidth: screen?.Width ?? 0,
                captureHeight: screen?.Height ?? 0,
                captureBytes: screen?.ImageBytes?.Length ?? vision?.ImageBytesLength ?? 0,
                visionStatusCode: vision?.VisionStatusCode,
                exceptionType: typeName,
                exceptionMessage: message,
                originMethod: pipelineCheckpoint);
        }

        private static AIGuideFailureStage ResolveStage(
            Exception ex,
            string checkpoint,
            string message,
            ScreenContext? screen,
            VisionAnalysisResult? vision)
        {
            if (ex is OperationCanceledException)
                return AIGuideFailureStage.Cancelled;

            if (checkpoint.Contains("CaptureTarget", StringComparison.OrdinalIgnoreCase)
                || (ex is NullReferenceException && checkpoint.Contains("Resolve", StringComparison.OrdinalIgnoreCase)))
                return AIGuideFailureStage.CaptureTargetResolve;

            if (checkpoint.Contains("Capture", StringComparison.OrdinalIgnoreCase)
                || ex is System.Windows.Markup.XamlParseException
                || message.Contains("RenderTargetBitmap", StringComparison.OrdinalIgnoreCase)
                || message.Contains("CopyFromScreen", StringComparison.OrdinalIgnoreCase))
            {
                if (screen != null && !screen.HasImage)
                    return screen.FailureStage != AIGuideFailureStage.None
                        ? screen.FailureStage
                        : AIGuideFailureStage.Capture;

                if (message.Contains("blank", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("black", StringComparison.OrdinalIgnoreCase)
                    || screen?.FailureStage == AIGuideFailureStage.BlankCapture)
                    return AIGuideFailureStage.BlankCapture;

                return AIGuideFailureStage.Capture;
            }

            if (checkpoint.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Vision API", StringComparison.OrdinalIgnoreCase)
                || message.Contains("api key", StringComparison.OrdinalIgnoreCase)
                || ex is System.Net.Http.HttpRequestException
                || ex is System.Text.Json.JsonException)
            {
                if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                    return AIGuideFailureStage.VisionTimeout;
                if (ex is System.Text.Json.JsonException
                    || message.Contains("json", StringComparison.OrdinalIgnoreCase))
                    return AIGuideFailureStage.VisionJsonParse;
                if (vision?.VisionStatusCode is >= 400)
                    return AIGuideFailureStage.VisionApiError;
                return AIGuideFailureStage.VisionRequest;
            }

            if (checkpoint.Contains("Context", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.ContextBuild;

            if (checkpoint.Contains("Summary", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.SmartSummary;

            if (checkpoint.Contains("Workflow", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.WorkflowDetection;

            if (checkpoint.Contains("Mentor", StringComparison.OrdinalIgnoreCase)
                || checkpoint.Contains("Dashboard", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.MentorBuild;

            if (ex is InvalidOperationException
                && (message.Contains("dispatcher", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("calling thread", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Cross-thread", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("thread it was created on", StringComparison.OrdinalIgnoreCase)))
                return AIGuideFailureStage.CaptureTargetResolve;

            if (ex is NullReferenceException)
            {
                if (checkpoint.Contains("Mentor", StringComparison.OrdinalIgnoreCase))
                    return AIGuideFailureStage.MentorBuild;
                if (checkpoint.Contains("Summary", StringComparison.OrdinalIgnoreCase))
                    return AIGuideFailureStage.SmartSummary;
                if (checkpoint.Contains("Vision", StringComparison.OrdinalIgnoreCase))
                    return AIGuideFailureStage.VisionRequest;
                if (checkpoint.Contains("Capture", StringComparison.OrdinalIgnoreCase))
                    return AIGuideFailureStage.Capture;
            }

            return AIGuideFailureStage.Unknown;
        }

        private static string ResolveUserMessage(AIGuideFailureStage stage, Exception ex, string message) =>
            stage switch
            {
                AIGuideFailureStage.CaptureTargetResolve => "No visible capture target.",
                AIGuideFailureStage.BlankCapture => RdpScreenCaptureProvider.BlankCaptureUserMessage,
                AIGuideFailureStage.Capture => "Screen capture failed.",
                AIGuideFailureStage.VisionTimeout => "Vision analysis timed out.",
                AIGuideFailureStage.VisionJsonParse => "Vision API response was not valid JSON.",
                AIGuideFailureStage.VisionApiError => "Vision API error.",
                AIGuideFailureStage.VisionRequest => "Vision analysis failed.",
                AIGuideFailureStage.SmartSummary => "Smart summary generation failed.",
                AIGuideFailureStage.WorkflowDetection => "Workflow detection failed.",
                AIGuideFailureStage.MentorBuild => "Mentor dashboard build failed.",
                AIGuideFailureStage.ContextBuild => "Context build failed.",
                AIGuideFailureStage.Cancelled => "Analysis cancelled.",
                _ => string.IsNullOrWhiteSpace(message) ? "Capture or analysis failed." : message
            };

        private static string ResolveSafeDetail(AIGuideFailureStage stage, Exception ex, string message)
        {
            if (stage == AIGuideFailureStage.BlankCapture)
                return RdpScreenCaptureProvider.BlankCaptureSafeDetail;

            if (!string.IsNullOrWhiteSpace(message) && message.Length <= 240)
                return message;

            return stage switch
            {
                AIGuideFailureStage.CaptureTargetResolve => "Switch to a server dashboard or connect an RDP session.",
                AIGuideFailureStage.Capture => "The capture target could not be rendered.",
                AIGuideFailureStage.VisionRequest => "The vision request could not complete.",
                AIGuideFailureStage.MentorBuild => "Building the mentor dashboard failed.",
                _ => ex.GetType().Name
            };
        }

        private static string BuildTechnicalMessage(Exception ex, string checkpoint) =>
            $"Checkpoint: {checkpoint}{Environment.NewLine}" +
            $"Type: {ex.GetType().FullName}{Environment.NewLine}" +
            $"Message: {ex.Message}{Environment.NewLine}" +
            $"Stack:{Environment.NewLine}{ex.StackTrace}";
    }
}
