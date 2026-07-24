using Netvium.Helpers;
using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide
{
    internal static class AIGuideAnalysisLogger
    {
        public static void CaptureRequested() =>
            Log("AIGuideCaptureRequested", "capture requested");

        public static void CaptureBlockedByRdpState(string state, string reason) =>
            Log("AIGuideCaptureBlockedByRdpState", $"state={state} reason={Sanitize(reason)}");

        public static void CaptureTargetResolving() =>
            Log("AIGuideCaptureTargetResolving", "resolving capture target on UI thread");

        public static void CaptureCrossThreadPrevented(string detail) =>
            Log("AIGuideCaptureCrossThreadPrevented", Sanitize(detail));

        public static void CaptureStarted() =>
            Log("AIGuideCaptureStarted", "background capture started");

        public static void CaptureAttemptSkippedBecauseRdpNotReady(string reason) =>
            Log("CaptureAttemptSkippedBecauseRdpNotReady", Sanitize(reason));

        public static void CaptureSkippedBecauseRdpNotStable(string reason) =>
            Log("CaptureSkippedBecauseRdpNotStable", Sanitize(reason));

        public static void ExistingAiGuideTaskCancelled() =>
            Log("ExistingAiGuideTaskCancelled", "in-flight AI Guide task cancelled for RDP connection");

        public static void CaptureFallbackAttempted(string method, double blankScore, string reason) =>
            Log("AIGuideCaptureFallback", $"method={method} blankScore={blankScore:0.#} reason={Sanitize(reason)}");

        public static void CaptureFailed(string stage, string method, double blankScore, bool timedOut) =>
            Log("AIGuideCaptureFailed", $"stage={stage} method={method} blankScore={blankScore:0.#} timedOut={timedOut}");

        public static void AnalysisStarted(long analysisId) =>
            Log("AIGuideAnalysisStarted", $"analysisId={analysisId}");

        public static void CaptureTargetResolved(long analysisId, string source, string target) =>
            Log("AIGuideCaptureTargetResolved", $"analysisId={analysisId} source={source} target={target}");

        public static void CaptureCompleted(long analysisId, int w, int h, int bytes, string method) =>
            Log("AIGuideCaptureCompleted", $"analysisId={analysisId} size={w}x{h} bytes={bytes} method={method}");

        public static void CaptureBlank(long analysisId, double blankScore, string method, string reason = "") =>
            Log("AIGuideCaptureBlank", $"analysisId={analysisId} blankScore={blankScore:0.#} method={method} reason={Sanitize(reason)}");

        public static void VisionRequestStarted(long analysisId) =>
            Log("AIGuideVisionRequestStarted", $"analysisId={analysisId}");

        public static void VisionRequestCompleted(long analysisId, long elapsedMs) =>
            Log("AIGuideVisionRequestCompleted", $"analysisId={analysisId} elapsedMs={elapsedMs}");

        public static void VisionFailed(long analysisId, string reason) =>
            Log("AIGuideVisionFailed", $"analysisId={analysisId} reason={Sanitize(reason)}");

        public static void SummaryStarted(long analysisId) =>
            Log("AIGuideSummaryStarted", $"analysisId={analysisId}");

        public static void SummaryFailed(long analysisId, string reason) =>
            Log("AIGuideSummaryFailed", $"analysisId={analysisId} reason={Sanitize(reason)}");

        public static void MentorBuildCompleted(long analysisId) =>
            Log("AIGuideMentorBuildCompleted", $"analysisId={analysisId}");

        public static void AnalysisFailed(long analysisId, string stage, string reason) =>
            Log("AIGuideAnalysisFailed", $"analysisId={analysisId} stage={stage} reason={Sanitize(reason)}");

        public static void StageTransition(
            string origin,
            string exceptionType,
            AIGuideFailureStage originalStage,
            AIGuideFailureStage currentStage,
            long analysisId) =>
            Log(
                "AIGuideStageTransition",
                $"origin={origin} exception={exceptionType} original={originalStage} current={currentStage} analysisId={analysisId}");

        public static void PipelineCheckpoint(long analysisId, int runId, string checkpoint) =>
            Log("AIGuidePipelineCheckpoint", $"analysisId={analysisId} runId={runId} checkpoint={checkpoint}");

        public static void PipelineException(
            string checkpoint,
            long analysisId,
            string exceptionType,
            string message,
            string? stackTrace)
        {
            Log(
                "AIGuidePipelineException",
                $"checkpoint={checkpoint} analysisId={analysisId} type={exceptionType} message={Sanitize(message)}");
            if (!string.IsNullOrWhiteSpace(stackTrace))
                Log("AIGuidePipelineExceptionStack", Sanitize(stackTrace));
        }

        public static void FreshCaptureStarted(string trigger) =>
            Log("FreshCaptureStarted", Sanitize(trigger));

        public static void FreshCaptureForQuestionStarted() =>
            Log("FreshCaptureForQuestionStarted", "user-question");

        public static void FreshCaptureForQuestionCompleted(bool success, long analysisId = 0) =>
            Log("FreshCaptureForQuestionCompleted", $"success={success} analysisId={analysisId}");

        public static void VisibleToolDetected(string tool) =>
            Log("VisibleToolDetected", Sanitize(tool));

        public static void UserIntentDetected(string intent) =>
            Log("UserIntentDetected", Sanitize(intent));

        public static void AnswerBlockedWrongTool(string visibleTool, string recommendedTool) =>
            Log("AnswerBlockedWrongTool", $"visible={Sanitize(visibleTool)} recommended={Sanitize(recommendedTool)}");

        public static void ModalDetected(string title, bool priority) =>
            Log("ModalDetected", $"title={Sanitize(title)} priority={priority}");

        public static void ExpertModuleSelected(string moduleId) =>
            Log("ExpertModuleSelected", Sanitize(moduleId));

        public static void WrongToolGuardTriggered(string visibleTool, string recommendedTool, string intent) =>
            Log("WrongToolGuardTriggered", $"visible={Sanitize(visibleTool)} recommended={Sanitize(recommendedTool)} intent={Sanitize(intent)}");

        public static void AnswerQualityCheckPassed(bool passed, string notes = "") =>
            Log("AnswerQualityCheckPassed", $"passed={passed} notes={Sanitize(notes)}");

        public static void ReasoningDecisionLogged(ReasoningDebugInfo info) =>
            Log(
                "ReasoningDecisionLogged",
                $"screen={Sanitize(info.ScreenType)} intent={Sanitize(info.Intent)} goal={Sanitize(info.Goal)} " +
                $"workflow={Sanitize(info.Workflow)} state={Sanitize(info.CurrentState)}->{Sanitize(info.NextState)} " +
                $"evidence={info.EvidenceCount} assumptions={info.AssumptionCount} " +
                $"reliability={info.ReliabilityScore}/{Sanitize(info.ReliabilityLabel)} " +
                $"confidence={Sanitize(info.ConfidenceLabel)} verified={info.VerificationPassed} " +
                $"reason={Sanitize(info.ReasonForDecision)}");

        public static void NavigationGraphBuilt(string path) =>
            Log("NavigationGraphBuilt", Sanitize(path));

        public static void NavigationGuidanceGenerated(string mode) =>
            Log("NavigationGuidanceGenerated", Sanitize(mode));

        public static void StaleContextCleared(string reason) =>
            Log("StaleContextCleared", Sanitize(reason));

        public static void FreshCaptureCompleted(bool success, long analysisId = 0) =>
            Log("FreshCaptureCompleted", $"success={success} analysisId={analysisId}");

        public static void ScreenFingerprintChanged(string reason, int hamming, double imageDiff) =>
            Log("ScreenFingerprintChanged", $"reason={Sanitize(reason)} hamming={hamming} imageDiff={imageDiff:0.#}");

        public static void PreviousContextCleared(string reason = "") =>
            Log("PreviousContextCleared", Sanitize(string.IsNullOrWhiteSpace(reason) ? "screen-changed" : reason));

        public static void NewScreenContextCreated(string reason = "") =>
            Log("NewScreenContextCreated", Sanitize(string.IsNullOrWhiteSpace(reason) ? "new-screen" : reason));

        public static void AnswerGeneratedFromFreshContext(long analysisId) =>
            Log("AnswerGeneratedFromFreshContext", $"analysisId={analysisId}");

        private static void Log(string action, string message) =>
            AppLogger.Debug(message, category: "AIGuide", action: action);

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Length > 200 ? value[..200] : value;
        }
    }
}
