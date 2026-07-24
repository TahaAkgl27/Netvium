using Netvium.Helpers;
using Netvium.Services;
using Netvium.Models;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class VisionAnalysisService
    {
        private readonly IVisionProvider _provider;

        public VisionAnalysisService(IVisionProvider? provider = null)
        {
            _provider = provider ?? new ConfigurableVisionProvider();
        }

        public async Task<VisionAnalysisResult> AnalyzeAsync(
            VisionRequest request,
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureEntitlementService.Instance.IsAiGuideVisionEnabled(settings))
            {
                return VisionAnalysisResult.Disabled(request.RequestId, request.Screen.Source);
            }

            var apiKey = SettingsApplicator.ResolveVisionApiKey(settings);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return VisionAnalysisResult.Failed(
                    request.RequestId,
                    request.Screen.Source,
                    "Vision API key is not configured.",
                    request.Screen.ImageBytes?.Length ?? 0,
                    AIGuideFailureStage.VisionRequest,
                    safeDetail: "Configure a Vision API key in Settings.");
            }

            if (request.Screen.ImageBytes == null || request.Screen.ImageBytes.Length == 0)
            {
                return VisionAnalysisResult.Failed(
                    request.RequestId,
                    request.Screen.Source,
                    "Screenshot is empty or unavailable.",
                    0,
                    AIGuideFailureStage.BlankCapture,
                    safeDetail: "Capture did not produce image bytes for vision analysis.");
            }

            var options = new VisionProviderOptions
            {
                Provider = string.IsNullOrWhiteSpace(settings.VisionProvider)
                    ? settings.AiProvider
                    : settings.VisionProvider,
                ApiKey = apiKey,
                Endpoint = settings.VisionEndpoint,
                Model = string.IsNullOrWhiteSpace(settings.VisionModel)
                    ? "gpt-4o-mini"
                    : settings.VisionModel,
                TimeoutSeconds = settings.VisionTimeoutSeconds,
                CaptureWarning = request.Screen.CaptureWarning
            };

            AppLogger.Debug(
                $"Vision analysis starting provider={options.Provider} model={options.Model}",
                category: "AIGuide",
                action: "VisionStart");

            return await _provider.AnalyzeAsync(request, options, cancellationToken).ConfigureAwait(false);
        }
    }
}
