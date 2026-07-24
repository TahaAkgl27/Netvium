using Netvium.Helpers;

using Netvium.Models;

using Netvium.Models.AIGuide;

using System.Text.Json;

using Netvium.Models.AIGuide.Decision;

using Netvium.Services.AIGuide.Decision;



using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide

{

    /// <summary>

    /// Orchestrates AI Guide screen capture and vision analysis.

    /// Observation-only — never controls the computer or executes commands.

    /// </summary>

    public sealed class AIGuideService

    {

        private readonly ScreenCaptureService _captureService;

        private readonly VisionAnalysisService _visionAnalysis;

        private readonly GuidanceEngineService _guidanceEngine;

        private readonly SmartSummaryService _smartSummary;

        private readonly WorkflowDetectionEngine _workflowEngine;

        private readonly VisualMentorEngine _visualMentorEngine;

        private readonly IWorkflowDecisionEngine _workflowDecisionEngine;

        private readonly WorkflowResponseGenerator _workflowResponseGenerator;

        private readonly object _gate = new();



        private ScreenContext? _currentScreen;

        private ConversationContext _conversation = new();

        private AIGuideSessionState _state = AIGuideSessionState.Idle;

        private VisionRequest? _pendingVisionRequest;

        private VisionAnalysisResult? _lastAnalysis;

        private RuntimeContext? _lastRuntimeContext;

        private SmartScreenSummary? _lastSmartSummary;

        private VisionObservation? _previousObservation;

        private WorkflowState _workflowState = WorkflowState.Empty();

        private int _activeStepIndex;

        private VisualMentorDashboard? _lastMentorDashboard;

        private ObservationTimeline _timeline = new();

        private long _activeAnalysisId;

        private string? _activeCaptureId;

        private string? _lastApplicationFingerprint;

        private ScreenVisualContext? _lastVisualContext;

        private ScreenFingerprint? _lastScreenFingerprint;

        private byte[]? _lastCaptureImageBytes;

        private bool _manualControlNoteShownThisSession;

        private WorkflowSessionState? _workflowSession;



        public long ActiveAnalysisId

        {

            get { lock (_gate) return _activeAnalysisId; }

        }



        public string? ActiveCaptureId

        {

            get { lock (_gate) return _activeCaptureId; }

        }



        public void CancelInFlightAnalysis()

        {

            lock (_gate)

            {

                _activeAnalysisId++;

                _activeCaptureId = null;

                _state = _lastAnalysis?.Success == true

                    ? AIGuideSessionState.Ready

                    : _lastAnalysis != null

                        ? AIGuideSessionState.Failed

                        : AIGuideSessionState.Idle;

            }

        }



        /// <summary>

        /// Clears all screen-derived memory before a new Ctrl+M capture. The visible screen is the source of truth.

        /// </summary>

        public void PrepareFreshCaptureSession()

        {

            lock (_gate)

            {

                ClearScreenContextLocked(clearApplicationFingerprint: true);

                _lastScreenFingerprint = null;

                _lastCaptureImageBytes = null;

            }

        }
        /// <summary>
        /// Starts a question from the pixels that will be captured next. Conversation turns are
        /// preserved, but observations, workflow anchors and runtime data from the prior screen
        /// are discarded so they cannot override the fresh screenshot.
        /// </summary>
        internal void PrepareFreshQuestionCapture()
        {
            lock (_gate)
            {
                // Preserve the conversation goal while discarding facts from the previous pixels.
                var preservedWorkflowGoal = _workflowSession;

                ClearScreenContextLocked(clearApplicationFingerprint: false);
                _workflowSession = preservedWorkflowGoal;
                _currentScreen = null;
                _pendingVisionRequest = null;
                _activeCaptureId = null;
            }

            AIGuideAnalysisLogger.PreviousContextCleared("fresh-user-question");
        }






        /// <summary>

        /// Clears screen-derived AI Guide state while preserving conversation turns.

        /// </summary>

        public void InvalidateScreenContext(string reason)

        {

            lock (_gate)

            {

                ClearScreenContextLocked(clearApplicationFingerprint: true);

                _lastScreenFingerprint = null;

                _lastCaptureImageBytes = null;

            }



            AIGuideAnalysisLogger.PreviousContextCleared(reason);

        }



        public AIGuideService(

            ScreenCaptureService? captureService = null,

            VisionAnalysisService? visionAnalysis = null,

            GuidanceEngineService? guidanceEngine = null,

            SmartSummaryService? smartSummary = null,

            WorkflowDetectionEngine? workflowEngine = null,

            VisualMentorEngine? visualMentorEngine = null,

            IWorkflowDecisionEngine? workflowDecisionEngine = null,

            WorkflowResponseGenerator? workflowResponseGenerator = null)

        {

            AIGuideBootstrap.TryEnsureReady(out _);

            _captureService = captureService ?? new ScreenCaptureService();

            _visionAnalysis = visionAnalysis ?? new VisionAnalysisService();

            _guidanceEngine = guidanceEngine ?? new GuidanceEngineService();

            _smartSummary = smartSummary ?? new SmartSummaryService();

            _workflowEngine = workflowEngine ?? new WorkflowDetectionEngine();

            _visualMentorEngine = visualMentorEngine ?? new VisualMentorEngine();

            _workflowDecisionEngine = workflowDecisionEngine ?? AIGuideCompositionRoot.CreateDecisionEngine();

            _workflowResponseGenerator = workflowResponseGenerator ?? new WorkflowResponseGenerator();

        }



        public AIGuideSessionState State

        {

            get { lock (_gate) return _state; }

        }



        public ScreenContext? CurrentScreen

        {

            get { lock (_gate) return _currentScreen; }

        }



        public ConversationContext Conversation

        {

            get { lock (_gate) return _conversation; }

        }



        public VisionRequest? PendingVisionRequest

        {

            get { lock (_gate) return _pendingVisionRequest; }

        }



        public VisionAnalysisResult? LastAnalysis

        {

            get { lock (_gate) return _lastAnalysis; }

        }



        public RuntimeContext? LastRuntimeContext

        {

            get { lock (_gate) return _lastRuntimeContext; }

        }



        public void SetRuntimeContext(RuntimeContext context)

        {

            lock (_gate)

            {

                _lastRuntimeContext = context;

            }

        }



        public SmartScreenSummary? LastSmartSummary

        {

            get { lock (_gate) return _lastSmartSummary; }

        }



        public WorkflowState LastWorkflowState

        {

            get { lock (_gate) return _workflowState; }

        }



        public WorkflowState DetectWorkflow()

        {

            WorkflowDetectionInput input;

            lock (_gate)

            {

                if (_lastAnalysis?.Observation == null)

                    return WorkflowState.Empty();



                input = new WorkflowDetectionInput

                {

                    CurrentObservation = _lastAnalysis.Observation,

                    PreviousObservation = _previousObservation,

                    RuntimeContext = _lastRuntimeContext,

                    CurrentSummary = _lastSmartSummary,

                    ConversationHistory = _conversation.Turns.ToList(),

                    PriorHistory = _workflowState.History,

                    ActiveWorkflow = _workflowState.Workflow,

                    ActiveStepIndex = _activeStepIndex

                };

            }



            var state = _workflowEngine.Detect(input);



            lock (_gate)

            {

                _workflowState = state;

                _activeStepIndex = state.Progress.CurrentStepIndex;

                if (_lastAnalysis?.Observation != null)

                    _previousObservation = SnapshotObservation(_lastAnalysis.Observation);

            }



            return state;

        }



        public VisualMentorDashboard? LastMentorDashboard

        {

            get { lock (_gate) return _lastMentorDashboard; }

        }



        public VisualMentorDashboard BuildVisualMentorDashboard(bool summaryGenerated = true)

        {

            VisualMentorInput input;

            lock (_gate)

            {

                if (_lastAnalysis?.Observation == null)

                    return new VisualMentorDashboard { IsReady = false, StatusBadge = "Not Ready" };



                input = new VisualMentorInput

                {

                    Observation = _lastAnalysis.Observation,

                    PreviousObservation = _previousObservation,

                    RuntimeContext = _lastRuntimeContext,

                    WorkflowState = _workflowState,

                    Summary = _lastSmartSummary,

                    ConversationHistory = _conversation.Turns.ToList(),

                    PriorTimeline = _timeline

                };

            }



            var dashboard = _visualMentorEngine.Build(input, summaryGenerated);



            lock (_gate)

            {

                _lastMentorDashboard = dashboard;

                _timeline = dashboard.Timeline;

            }



            return dashboard;

        }



        public async Task<SmartScreenSummary> GenerateSmartSummaryAsync(

            AppSettings settings,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            VisionAnalysisResult? analysis;

            RuntimeContext? runtimeContext;

            lock (_gate)

            {

                if (_state != AIGuideSessionState.Ready || _lastAnalysis?.Observation == null)

                {

                    return SmartScreenSummary.Failed(

                        Guid.NewGuid().ToString("N"),

                        "Complete screen analysis before generating a summary.");

                }



                analysis = _lastAnalysis;

                runtimeContext = _lastRuntimeContext;

                _state = AIGuideSessionState.Summarizing;

                _lastSmartSummary = null;

            }



            var requestId = Guid.NewGuid().ToString("N");

            var request = new SmartSummaryRequest

            {

                RequestId = requestId,

                Observation = analysis!.Observation!,

                ObservationJson = analysis.RawJson,

                RuntimeContext = runtimeContext

            };



            SmartScreenSummary? summary = null;

            try

            {

                summary = await _smartSummary.GenerateAsync(request, settings, ct).ConfigureAwait(false);

            }

            catch (OperationCanceledException)

            {

                throw;

            }

            finally

            {

                lock (_gate)

                {

                    if (_state == AIGuideSessionState.Summarizing)

                        _state = _lastAnalysis?.Success == true

                            ? AIGuideSessionState.Ready

                            : AIGuideSessionState.Failed;

                }

            }



            if (summary == null)

                return SmartScreenSummary.Failed(requestId, "Summary generation did not complete.");



            lock (_gate)

            {

                _lastSmartSummary = summary;

                _state = AIGuideSessionState.Ready;

            }



            return summary;

        }



        public async Task<SmartScreenSummary> GenerateSmartSummaryAsync(

            AppSettings settings,

            long analysisId,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            if (!IsAnalysisCurrent(analysisId))

                return SmartScreenSummary.Failed(Guid.NewGuid().ToString("N"), "Analysis superseded.");



            return await GenerateSmartSummaryAsync(settings, ct).ConfigureAwait(false);

        }



        public async Task<(ScreenContext Screen, long AnalysisId)> CaptureForAnalysisAsync(

            ScreenCaptureOptions options,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            long analysisId;

            lock (_gate)

            {

                analysisId = ++_activeAnalysisId;

                _activeCaptureId = null;

                _state = AIGuideSessionState.Capturing;

                _pendingVisionRequest = null;

            }



            ScreenContext screen;

            try

            {

                screen = await _captureService.CaptureAsync(options, ct).ConfigureAwait(false);

            }

            catch (Exception ex)

            {

                AppLogger.Error("AI Guide capture failed.", ex, category: "AIGuide", action: "Capture");

                screen = new ScreenContext

                {

                    CaptureId = Guid.NewGuid().ToString("N"),

                    Source = options.Source,

                    MonitorIndex = options.MonitorIndex,

                    ServerKey = options.ServerKey,

                    ServerName = options.ServerName,

                    CaptureWarning = ex.Message,

                    FailureStage = AIGuideFailureStage.Capture

                };

            }



            ScreenVisualContext? visualContext = null;

            if (screen.HasImage)

            {

                string? previousFingerprint;

                lock (_gate) { previousFingerprint = _lastApplicationFingerprint; }



                visualContext = await ScreenVisualContextExtractor

                    .ExtractAsync(screen, previousFingerprint, ct)

                    .ConfigureAwait(false);



                screen.VisualContext = visualContext;



                if (visualContext.ApplicationChanged)

                {

                    AppLogger.Debug(

                        "AI Guide application changed — discarding prior visual context.",

                        category: "AIGuide",

                        action: "ApplicationChanged");



                    lock (_gate)

                    {

                        ClearScreenContextLocked(clearApplicationFingerprint: true);

                    }



                    AIGuideAnalysisLogger.StaleContextCleared("application-changed");
                    AIGuideAnalysisLogger.PreviousContextCleared("application-changed");

                }

            }



            if (screen.HasImage && screen.ImageBytes != null)

            {

                ProcessScreenFingerprintAfterCapture(screen, visualContext);

            }



            lock (_gate)

            {

                if (analysisId != _activeAnalysisId)

                    return (screen, analysisId);



                _currentScreen = screen;

                _lastVisualContext = visualContext;

                if (visualContext != null)

                    _lastApplicationFingerprint = visualContext.ApplicationFingerprint;



                _activeCaptureId = screen.CaptureId;

                _conversation.ActiveServerKey = options.ServerKey;

                _state = AIGuideSessionState.Analyzing;

                _pendingVisionRequest = BuildVisionRequestLocked(userPrompt: null, isFreshAnalysis: true);

            }



            AppLogger.Debug(

                $"AI Guide captured {screen.Source} {screen.Width}x{screen.Height}",

                category: "AIGuide",

                action: "CaptureReady");



            return (screen, analysisId);

        }



        public async Task<VisionAnalysisResult> AnalyzeCapturedScreenAsync(

            AppSettings settings,

            long analysisId,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            VisionRequest? request;

            lock (_gate)

            {

                if (analysisId != _activeAnalysisId)

                    return VisionAnalysisResult.Failed(

                        Guid.NewGuid().ToString("N"),

                        _currentScreen?.Source ?? ScreenCaptureSource.Unknown,

                        "Analysis superseded.",

                        stage: AIGuideFailureStage.Cancelled,

                        safeDetail: "A newer analysis replaced this one.");



                request = _pendingVisionRequest;

                if (request == null)

                    return VisionAnalysisResult.Failed(

                        Guid.NewGuid().ToString("N"),

                        _currentScreen?.Source ?? ScreenCaptureSource.Unknown,

                        "No captured screen is pending analysis.",

                        stage: AIGuideFailureStage.Capture,

                        safeDetail: "Screen capture did not register a pending vision request.");

            }



            var result = await _visionAnalysis.AnalyzeAsync(request, settings, ct).ConfigureAwait(false);



            if (result.Success && result.Observation != null && request.Screen.VisualContext != null)

            {

                result = ScreenVisualContextEnricher.Enrich(result, request.Screen.VisualContext);

            }



            lock (_gate)

            {

                if (analysisId != _activeAnalysisId)

                    return result;



                _lastAnalysis = result;

                _state = result.Success ? AIGuideSessionState.Ready : AIGuideSessionState.Failed;

            }



            return result;

        }



        public bool IsAnalysisCurrent(long analysisId, string? captureId = null)

        {

            lock (_gate)

            {

                if (analysisId != _activeAnalysisId)

                    return false;

                if (captureId != null && !string.Equals(_activeCaptureId, captureId, StringComparison.Ordinal))

                    return false;

                return true;

            }

        }



        public async Task<VisionAnalysisResult> CaptureAndAnalyzeAsync(

            ScreenCaptureOptions options,

            AppSettings settings,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            var (screen, analysisId) = await CaptureForAnalysisAsync(options, ct).ConfigureAwait(false);

            if (!IsAnalysisCurrent(analysisId, screen.CaptureId))

            {

                return VisionAnalysisResult.Failed(

                    Guid.NewGuid().ToString("N"),

                    screen.Source,

                    "Analysis superseded.");

            }



            return await AnalyzeCapturedScreenAsync(settings, analysisId, ct).ConfigureAwait(false);

        }



        public async Task<GuidanceResponse> AskGuidanceWithFreshCaptureAsync(

            string userQuestion,

            ScreenCaptureOptions captureOptions,

            AppSettings settings,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            AIGuideAnalysisLogger.FreshCaptureStarted("user-question");
            AIGuideAnalysisLogger.FreshCaptureForQuestionStarted();

            var trimmedQuestion = userQuestion.Trim();

            if (string.IsNullOrWhiteSpace(trimmedQuestion))

                return GuidanceResponse.Failed(Guid.NewGuid().ToString("N"), AIGuideLanguage.EnterQuestionHint);



            // Every message is screen-aware. Never answer a new question from the prior capture.
            PrepareFreshQuestionCapture();

            long analysisId;

            lock (_gate)

            {

                analysisId = ++_activeAnalysisId;

                _activeCaptureId = null;

                _state = AIGuideSessionState.Capturing;

                _pendingVisionRequest = null;

            }



            ScreenContext screen;

            try

            {

                screen = await _captureService.CaptureAsync(captureOptions, ct).ConfigureAwait(false);

            }

            catch (Exception ex)

            {

                AppLogger.Error("AI Guide question capture failed.", ex, category: "AIGuide", action: "QuestionCapture");

                AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(false, analysisId);

                lock (_gate)

                {

                    _state = _lastAnalysis?.Success == true

                        ? AIGuideSessionState.Ready

                        : AIGuideSessionState.Failed;

                }

                return GuidanceResponse.Failed(

                    Guid.NewGuid().ToString("N"),

                    AIGuideLanguage.FreshCaptureFailedNoFallback);

            }



            if (!screen.HasImage

                || screen.ImageBytes == null

                || screen.ImageBytes.Length == 0

                || CaptureImageAnalyzer.IsMostlyBlank(screen.ImageBytes))

            {

                AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(false, analysisId);

                lock (_gate)

                {

                    _state = _lastAnalysis?.Success == true

                        ? AIGuideSessionState.Ready

                        : AIGuideSessionState.Failed;

                }

                return GuidanceResponse.Failed(

                    Guid.NewGuid().ToString("N"),

                    AIGuideLanguage.FreshCaptureFailedNoFallback);

            }



            string? previousApplicationFingerprint;

            ScreenFingerprint? previousScreenFingerprint;

            byte[]? previousImageBytes;

            lock (_gate)

            {

                previousApplicationFingerprint = _lastApplicationFingerprint;

                previousScreenFingerprint = _lastScreenFingerprint;

                previousImageBytes = _lastCaptureImageBytes;

            }



            ScreenVisualContext? visualContext = await ScreenVisualContextExtractor

                .ExtractAsync(screen, previousApplicationFingerprint, ct)

                .ConfigureAwait(false);

            screen.VisualContext = visualContext;



            var screenFingerprint = ScreenFingerprintBuilder.Build(screen.ImageBytes, visualContext);

            var change = ScreenChangeDetector.Compare(

                previousScreenFingerprint,

                screenFingerprint,

                previousImageBytes,

                screen.ImageBytes);



            if (change.IsNewScreen)

            {

                if (previousScreenFingerprint != null)

                {

                    AIGuideAnalysisLogger.ScreenFingerprintChanged(

                        change.Reason,

                        change.HashHammingDistance,

                        change.ImageDifferenceScore);

                    lock (_gate) { ClearScreenContextLocked(clearApplicationFingerprint: true); }

                    AIGuideAnalysisLogger.StaleContextCleared(change.Reason);
                    AIGuideAnalysisLogger.PreviousContextCleared(change.Reason);

                }

                else

                {

                    AIGuideAnalysisLogger.NewScreenContextCreated(change.Reason);

                }

            }



            lock (_gate)

            {

                if (analysisId != _activeAnalysisId)

                {

                    AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(false, analysisId);

                    return GuidanceResponse.Failed(

                        Guid.NewGuid().ToString("N"),

                        AIGuideLanguage.FreshCaptureFailedNoFallback);

                }



                _currentScreen = screen;

                _lastVisualContext = visualContext;

                _lastScreenFingerprint = screenFingerprint;

                _lastCaptureImageBytes = screen.ImageBytes;

                if (visualContext != null)

                    _lastApplicationFingerprint = visualContext.ApplicationFingerprint;



                _activeCaptureId = screen.CaptureId;

                _conversation.ActiveServerKey = captureOptions.ServerKey;

                _state = AIGuideSessionState.Analyzing;

                _pendingVisionRequest = BuildVisionRequestLocked(userPrompt: trimmedQuestion, isFreshAnalysis: true);

            }



            VisionRequest? visionRequest;

            lock (_gate) { visionRequest = _pendingVisionRequest; }

            if (visionRequest == null)

            {

                AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(false, analysisId);

                lock (_gate) { _state = AIGuideSessionState.Failed; }

                return GuidanceResponse.Failed(

                    Guid.NewGuid().ToString("N"),

                    AIGuideLanguage.FreshCaptureFailedNoFallback);

            }



            var visionResult = await _visionAnalysis.AnalyzeAsync(visionRequest, settings, ct).ConfigureAwait(false);



            if (visionResult.Success && visionResult.Observation != null && screen.VisualContext != null)

                visionResult = ScreenVisualContextEnricher.Enrich(visionResult, screen.VisualContext);



            if (!visionResult.Success || visionResult.Observation == null)

            {

                AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(false, analysisId);

                lock (_gate) { _state = AIGuideSessionState.Failed; }

                return GuidanceResponse.Failed(

                    Guid.NewGuid().ToString("N"),

                    AIGuideLanguage.FreshCaptureFailedNoFallback);

            }



            lock (_gate)

            {

                if (analysisId != _activeAnalysisId)

                {

                    AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(false, analysisId);

                    return GuidanceResponse.Failed(

                        Guid.NewGuid().ToString("N"),

                        AIGuideLanguage.FreshCaptureFailedNoFallback);

                }



                _lastAnalysis = visionResult;

                _state = AIGuideSessionState.Guiding;

            }



            AIGuideAnalysisLogger.FreshCaptureForQuestionCompleted(true, analysisId);



            var guidanceResponse = await AskGuidanceInternalAsync(

                trimmedQuestion,

                visionResult,

                visualContext,

                settings,

                ct).ConfigureAwait(false);



            if (guidanceResponse.Success)

                AIGuideAnalysisLogger.AnswerGeneratedFromFreshContext(analysisId);





            return guidanceResponse;

        }



        public async Task<GuidanceResponse> AskGuidanceAsync(

            string userQuestion,

            AppSettings settings,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            VisionAnalysisResult? analysis;

            ScreenVisualContext? visualContext;

            lock (_gate)

            {

                if (_state != AIGuideSessionState.Ready || _lastAnalysis?.Observation == null)

                {

                    return GuidanceResponse.NotReady(Guid.NewGuid().ToString("N"));

                }



                analysis = _lastAnalysis;

                visualContext = _lastVisualContext;

            }



            var requestId = Guid.NewGuid().ToString("N");

            var trimmedQuestion = userQuestion.Trim();

            if (string.IsNullOrWhiteSpace(trimmedQuestion))

            {

                lock (_gate) { _state = AIGuideSessionState.Ready; }

                return GuidanceResponse.Failed(requestId, AIGuideLanguage.EnterQuestionHint);

            }



            return await AskGuidanceInternalAsync(

                trimmedQuestion,

                analysis!,

                visualContext,

                settings,

                ct).ConfigureAwait(false);

        }



        private async Task<GuidanceResponse> AskGuidanceInternalAsync(

            string trimmedQuestion,

            VisionAnalysisResult analysis,

            ScreenVisualContext? visualContext,

            AppSettings settings,

            CancellationToken ct,

            GuidanceRequest? preparedRequest = null,

            string? preparedRequestId = null)

        {

            var requestId = preparedRequestId ?? Guid.NewGuid().ToString("N");

            RuntimeContext? runtimeContext;

            WorkflowState? workflowState;

            List<ConversationTurn> priorTurns;

            WorkflowSessionState? workflowSession;

            lock (_gate)

            {

                runtimeContext = _lastRuntimeContext;

                workflowState = _workflowState;

                priorTurns = _conversation.Turns.ToList();

                workflowSession = _workflowSession;

                _state = AIGuideSessionState.Guiding;

            }



            lock (_gate)

            {

                _conversation.Turns.Add(new ConversationTurn

                {

                    Role = "user",

                    Content = AIGuideSecretRedactor.Redact(trimmedQuestion)

                });

                _visualMentorEngine.Timeline.AppendUserQuestion(_timeline, trimmedQuestion);

            }



            var workflowDecision = _workflowDecisionEngine.Decide(new WorkflowDecisionInput

            {

                UserQuestion = trimmedQuestion,

                VisualContext = visualContext,

                Observation = analysis.Observation,

                PriorTurns = priorTurns,

                SessionState = workflowSession

            });



            if (workflowDecision.DebugInfo != null)

                AIGuideAnalysisLogger.ReasoningDecisionLogged(workflowDecision.DebugInfo);



            var toolGuidance = LegacyGuidanceMapper.ToToolHint(workflowDecision, visualContext, trimmedQuestion);

            var navigationGuidance = LegacyGuidanceMapper.ToNavigationHint(workflowDecision);

            var request = preparedRequest ?? new GuidanceRequest

            {

                RequestId = requestId,

                UserQuestion = trimmedQuestion,

                Observation = analysis.Observation!,

                ObservationJson = analysis.RawJson,

                CaptureSource = analysis.CaptureSource,

                ServerName = _currentScreen?.ServerName,

                ServerKey = _currentScreen?.ServerKey ?? _conversation.ActiveServerKey,

                AnalyzedAtUtc = analysis.AnalyzedAtUtc,

                RuntimeContext = runtimeContext,

                WorkflowState = workflowState,

                PriorTurns = priorTurns,

                VisualContext = visualContext,

                IsFreshVisualContext = true,
                ScreenshotImageBytes = _currentScreen?.ImageBytes,
                ScreenshotWidth = _currentScreen?.Width ?? 0,
                ScreenshotHeight = _currentScreen?.Height ?? 0,
                GuideMode = "ObserveOnly",


                ToolGuidance = toolGuidance,

                IncludeRuntimeContext = AdminToolExpertEngine.QuestionMentionsConnection(trimmedQuestion),

                SuppressManualControlNote = _manualControlNoteShownThisSession,

                ExpertModuleId = toolGuidance.ExpertModuleId,

                ExpertKnowledge = toolGuidance.ExpertKnowledge,

                NavigationGuidance = navigationGuidance,

                WorkflowContext = workflowDecision.Context,

                WorkflowDecision = workflowDecision

            };



            if (!string.IsNullOrWhiteSpace(request.ToolGuidance?.VisibleToolName))

                AIGuideAnalysisLogger.VisibleToolDetected(request.ToolGuidance.VisibleToolName);

            if (visualContext?.HasModalDialog == true || visualContext?.ModalPriority == true)

                AIGuideAnalysisLogger.ModalDetected(visualContext?.ModalTitle ?? visualContext?.VisibleDialogTitle ?? "modal", visualContext?.ModalPriority == true);

            if (request.ToolGuidance?.UserIntent != AdminUserIntent.Unknown)

                AIGuideAnalysisLogger.UserIntentDetected(request.ToolGuidance!.UserIntent.ToString());

            if (!string.IsNullOrWhiteSpace(request.ToolGuidance?.ExpertModuleId))

                AIGuideAnalysisLogger.ExpertModuleSelected(request.ToolGuidance!.ExpertModuleId);

            if (request.NavigationGuidance != null && request.NavigationGuidance.NavigationPath.Count > 0)

                AIGuideAnalysisLogger.NavigationGraphBuilt(string.Join(" > ", request.NavigationGuidance.NavigationPath));

            if (workflowDecision.UseLocalResponse)

                AIGuideAnalysisLogger.NavigationGuidanceGenerated("decision-engine");

            else if (!string.IsNullOrWhiteSpace(request.NavigationGuidance?.PromptDirective))

                AIGuideAnalysisLogger.NavigationGuidanceGenerated("llm-naturalize");

            if (request.ToolGuidance?.IsToolMismatch == true)

                AIGuideAnalysisLogger.WrongToolGuardTriggered(

                    request.ToolGuidance.VisibleToolName,

                    request.ToolGuidance.RecommendedToolName,

                    request.ToolGuidance.UserIntent.ToString());



            GuidanceResponse response;

            try

            {

                var useImageFirstGuidance = request.HasScreenshot
                    && !workflowDecision.ShouldRefuse
                    && !workflowDecision.Context.IsClarification
                    && !workflowDecision.Context.IsWrongScreen;
                if (workflowDecision.UseLocalResponse && !useImageFirstGuidance)

                {

                    response = _workflowResponseGenerator.Generate(

                        workflowDecision,

                        requestId,

                        request.SuppressManualControlNote);

                }

                else

                {

                    response = await _guidanceEngine.AskAsync(request, settings, ct).ConfigureAwait(false);

                }

            }

            catch (OperationCanceledException)

            {

                lock (_gate) { _state = AIGuideSessionState.Ready; }

                throw;

            }



            if (response.Success && request.ToolGuidance != null)

            {

                response = ApplySessionPolicies(response, request);

                var quality = AnswerQualityChecker.Check(

                    response,

                    request.ToolGuidance,

                    trimmedQuestion,

                    request.IsFreshVisualContext);

                AIGuideAnalysisLogger.AnswerQualityCheckPassed(

                    quality.Passed,

                    quality.Notes.Count > 0 ? string.Join(",", quality.Notes) : string.Empty);

            }



            lock (_gate)

            {

                if (response.Success)

                {

                    _conversation.Turns.Add(new ConversationTurn

                    {

                        Role = "assistant",

                        Content = response.FormattedAnswer

                    });

                    _visualMentorEngine.Timeline.AppendAnswer(_timeline);

                    if (workflowDecision.Context.Intent != AdminUserIntent.Unknown
                        && !workflowDecision.Context.IsClarification
                        && !workflowDecision.Context.IsWrongScreen
                        && !workflowDecision.ShouldRefuse)
                    {
                        var anchor = ConversationContinuityService.LooksLikeContinuation(trimmedQuestion)
                            && !string.IsNullOrWhiteSpace(_workflowSession?.AnchorQuestion)
                            ? _workflowSession.AnchorQuestion
                            : trimmedQuestion;
                        _workflowSession = WorkflowSessionState.FromDecision(workflowDecision, anchor);
                    }

                }



                _state = AIGuideSessionState.Ready;

            }



            return response;

        }



        private GuidanceResponse ApplySessionPolicies(GuidanceResponse response, GuidanceRequest request)

        {

            if (request.SuppressManualControlNote)

            {

                var withoutNote = new GuidanceResponse

                {

                    RequestId = response.RequestId,

                    Success = true,

                    CurrentScreen = response.CurrentScreen,
                    DirectAnswer = response.DirectAnswer,

                    WhatICanSee = response.WhatICanSee,

                    ManualSteps = response.ManualSteps,

                    IfYouDoNotSeeIt = response.IfYouDoNotSeeIt,
                    VisibilityNote = response.VisibilityNote,

                    ImportantNote = string.Empty,

                    Confidence = response.Confidence,

                    ConfidenceDetail = response.ConfidenceDetail,

                    VisibleEvidence = response.VisibleEvidence

                };

                withoutNote.FormattedAnswer = GuidanceResponseFormatter.Format(withoutNote);

                return withoutNote;

            }



            if (!string.IsNullOrWhiteSpace(response.ImportantNote))
                lock (_gate) { _manualControlNoteShownThisSession = true; }

            return response;

        }



        public async Task<ScreenContext> CaptureAndPrepareAsync(

            ScreenCaptureOptions options,

            CancellationToken ct = default)

        {

            AiGuideEntitlement.EnsureEnabled();

            lock (_gate)

            {

                _state = AIGuideSessionState.Capturing;

                _pendingVisionRequest = null;

            }



            ScreenContext screen;

            try

            {

                screen = await _captureService.CaptureAsync(options, ct).ConfigureAwait(false);

            }

            catch (Exception ex)

            {

                AppLogger.Error("AI Guide capture failed.", ex, category: "AIGuide", action: "Capture");

                screen = new ScreenContext

                {

                    CaptureId = Guid.NewGuid().ToString("N"),

                    Source = options.Source,

                    MonitorIndex = options.MonitorIndex,

                    ServerKey = options.ServerKey,

                    ServerName = options.ServerName,

                    CaptureWarning = ex.Message,

                    FailureStage = AIGuideFailureStage.Capture

                };

            }



            ScreenVisualContext? visualContext = null;

            if (screen.HasImage)

            {

                string? previousFingerprint;

                lock (_gate) { previousFingerprint = _lastApplicationFingerprint; }



                visualContext = await ScreenVisualContextExtractor

                    .ExtractAsync(screen, previousFingerprint, ct)

                    .ConfigureAwait(false);

                screen.VisualContext = visualContext;



                if (visualContext.ApplicationChanged)

                {

                    lock (_gate) { ClearScreenContextLocked(clearApplicationFingerprint: true); }

                    AIGuideAnalysisLogger.StaleContextCleared("application-changed");
                    AIGuideAnalysisLogger.PreviousContextCleared("application-changed");

                }

            }



            if (screen.HasImage && screen.ImageBytes != null)

                ProcessScreenFingerprintAfterCapture(screen, visualContext);



            lock (_gate)

            {

                _currentScreen = screen;

                _lastVisualContext = visualContext;

                if (visualContext != null)

                    _lastApplicationFingerprint = visualContext.ApplicationFingerprint;



                _conversation.ActiveServerKey = options.ServerKey;

                _state = AIGuideSessionState.Analyzing;

                _pendingVisionRequest = BuildVisionRequestLocked(userPrompt: null, isFreshAnalysis: true);

            }



            return screen;

        }



        public VisionRequest? BuildVisionRequest(string? userPrompt = null)

        {

            lock (_gate)

                return BuildVisionRequestLocked(userPrompt, isFreshAnalysis: false);

        }



        public void ClearSession()

        {

            lock (_gate)

            {

                ClearScreenContextLocked(clearApplicationFingerprint: true);

                _conversation = new ConversationContext();

                _lastScreenFingerprint = null;

                _lastCaptureImageBytes = null;

                _currentScreen = null;

                _pendingVisionRequest = null;

                _activeAnalysisId++;

                _activeCaptureId = null;

                _state = AIGuideSessionState.Idle;

            }

        }



        private void ProcessScreenFingerprintAfterCapture(ScreenContext screen, ScreenVisualContext? visualContext)

        {

            if (screen.ImageBytes == null)

                return;



            var newFingerprint = ScreenFingerprintBuilder.Build(screen.ImageBytes, visualContext);

            ScreenFingerprint? previousFingerprint;

            byte[]? previousImageBytes;

            lock (_gate)

            {

                previousFingerprint = _lastScreenFingerprint;

                previousImageBytes = _lastCaptureImageBytes;

            }



            var change = ScreenChangeDetector.Compare(

                previousFingerprint,

                newFingerprint,

                previousImageBytes,

                screen.ImageBytes);



            if (change.IsNewScreen)

            {

                if (previousFingerprint != null)

                {

                    AIGuideAnalysisLogger.ScreenFingerprintChanged(

                        change.Reason,

                        change.HashHammingDistance,

                        change.ImageDifferenceScore);

                    lock (_gate) { ClearScreenContextLocked(clearApplicationFingerprint: true); }

                    AIGuideAnalysisLogger.StaleContextCleared(change.Reason);
                    AIGuideAnalysisLogger.PreviousContextCleared(change.Reason);

                }

                else

                {

                    AIGuideAnalysisLogger.NewScreenContextCreated(change.Reason);

                }

            }



            lock (_gate)

            {

                _lastScreenFingerprint = newFingerprint;

                _lastCaptureImageBytes = screen.ImageBytes;

            }

        }



        private void ClearScreenContextLocked(bool clearApplicationFingerprint)

        {

            _lastAnalysis = null;

            _lastRuntimeContext = null;

            _lastSmartSummary = null;

            _previousObservation = null;

            _workflowState = WorkflowState.Empty();

            _activeStepIndex = 0;

            _workflowSession = null;

            _lastMentorDashboard = null;

            _timeline = new ObservationTimeline();

            _lastVisualContext = null;



            if (clearApplicationFingerprint)

                _lastApplicationFingerprint = null;

        }



        private static VisionObservation? SnapshotObservation(VisionObservation observation)

        {

            return JsonSerializer.Deserialize<VisionObservation>(JsonSerializer.Serialize(observation));

        }



        private VisionRequest? BuildVisionRequestLocked(string? userPrompt, bool isFreshAnalysis)

        {

            if (_currentScreen == null)

                return null;



            var conversation = isFreshAnalysis

                ? new ConversationContext { ActiveServerKey = _conversation.ActiveServerKey }

                : _conversation;



            return new VisionRequest

            {

                RequestId = Guid.NewGuid().ToString("N"),

                Screen = _currentScreen,

                Conversation = conversation,

                UserPrompt = userPrompt,

                ReadOnlyAnalysis = true,

                IsFreshAnalysis = isFreshAnalysis

            };

        }

    }

}

