using System.Text.Json;
using Netvium.Services;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class GuidanceEngineService
    {
        private readonly IGuidanceProvider _provider;

        public GuidanceEngineService(IGuidanceProvider? provider = null)
        {
            _provider = provider ?? new ConfigurableGuidanceProvider();
        }

        public async Task<GuidanceResponse> AskAsync(
            GuidanceRequest request,
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureEntitlementService.Instance.IsAiGuideVisionEnabled(settings))
                return GuidanceResponse.Failed(request.RequestId, "AI Guide vision is disabled in settings.");

            // The guidance call is multimodal, so it must use the configured vision provider/model/key.
            var apiKey = SettingsApplicator.ResolveVisionApiKey(settings);
            if (string.IsNullOrWhiteSpace(apiKey))
                return GuidanceResponse.Failed(request.RequestId, "Vision API key is not configured.");

            var redactedQuestion = AIGuideSecretRedactor.Redact(request.UserQuestion);
            var observationJson = RedactObservationJson(request.ObservationJson, request.Observation);

            var safeRequest = new GuidanceRequest
            {
                RequestId = request.RequestId,
                UserQuestion = redactedQuestion,
                Observation = request.Observation,
                ObservationJson = observationJson,
                CaptureSource = request.CaptureSource,
                ServerName = request.ServerName,
                ServerKey = request.ServerKey,
                AnalyzedAtUtc = request.AnalyzedAtUtc,
                RuntimeContext = request.RuntimeContext,
                WorkflowState = request.WorkflowState,
                PriorTurns = request.PriorTurns,
                VisualContext = request.VisualContext,
                IsFreshVisualContext = request.IsFreshVisualContext,
                ScreenshotImageBytes = request.ScreenshotImageBytes,
                ScreenshotWidth = request.ScreenshotWidth,
                ScreenshotHeight = request.ScreenshotHeight,
                GuideMode = "ObserveOnly",
                ToolGuidance = request.ToolGuidance,
                IncludeRuntimeContext = request.IncludeRuntimeContext,
                SuppressManualControlNote = request.SuppressManualControlNote,
                ExpertModuleId = request.ExpertModuleId ?? request.ToolGuidance?.ExpertModuleId,
                ExpertKnowledge = request.ExpertKnowledge ?? request.ToolGuidance?.ExpertKnowledge,
                NavigationGuidance = request.NavigationGuidance,
                WorkflowContext = request.WorkflowContext,
                WorkflowDecision = request.WorkflowDecision
            };

            AppLogger.Debug(
                $"Guidance request starting imageAttached={safeRequest.HasScreenshot} size={safeRequest.ScreenshotWidth}x{safeRequest.ScreenshotHeight}.",
                category: "AIGuide",
                action: "GuidanceStart");

            var options = new GuidanceProviderOptions
            {
                Provider = string.IsNullOrWhiteSpace(settings.VisionProvider) ? settings.AiProvider : settings.VisionProvider,
                ApiKey = apiKey,
                Endpoint = settings.VisionEndpoint,
                Model = string.IsNullOrWhiteSpace(settings.VisionModel)
                    ? (string.IsNullOrWhiteSpace(settings.AiModel) ? "gpt-4o-mini" : settings.AiModel)
                    : settings.VisionModel,
                TimeoutSeconds = settings.VisionTimeoutSeconds
            };

            return await _provider.GenerateAsync(safeRequest, options, cancellationToken).ConfigureAwait(false);
        }

        private static string RedactObservationJson(string? observationJson, VisionObservation observation)
        {
            if (!string.IsNullOrWhiteSpace(observationJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<VisionObservation>(observationJson);
                    if (parsed != null)
                    {
                        RedactObservation(parsed);
                        return JsonSerializer.Serialize(parsed);
                    }
                }
                catch
                {
                    // Fall through to serialize from object.
                }
            }

            RedactObservation(observation);
            return JsonSerializer.Serialize(observation);
        }

        private static void RedactObservation(VisionObservation observation)
        {
            observation.WindowTitle = AIGuideSecretRedactor.Redact(observation.WindowTitle);
            observation.CurrentTaskHint = AIGuideSecretRedactor.Redact(observation.CurrentTaskHint);
            foreach (var block in observation.VisibleText) block.Text = AIGuideSecretRedactor.Redact(block.Text);
            foreach (var control in observation.VisibleControls) control.Label = AIGuideSecretRedactor.Redact(control.Label);
            foreach (var err in observation.VisibleErrors) err.Message = AIGuideSecretRedactor.Redact(err.Message);
            foreach (var warn in observation.VisibleWarnings) warn.Message = AIGuideSecretRedactor.Redact(warn.Message);
        }
    }
}
