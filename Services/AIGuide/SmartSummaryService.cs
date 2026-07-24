using System.Text.Json;
using Netvium.Services;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class SmartSummaryService
    {
        private readonly ISmartSummaryProvider _provider;

        public SmartSummaryService(ISmartSummaryProvider? provider = null)
        {
            _provider = provider ?? new ConfigurableSmartSummaryProvider();
        }

        public async Task<SmartScreenSummary> GenerateAsync(
            SmartSummaryRequest request,
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureEntitlementService.Instance.IsAiGuideVisionEnabled(settings))
            {
                return SmartSummaryFallbackBuilder.Build(
                    request.RequestId,
                    request.Observation,
                    request.RuntimeContext);
            }

            var apiKey = SettingsApplicator.ResolveAiApiKey(settings);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return SmartSummaryFallbackBuilder.Build(
                    request.RequestId,
                    request.Observation,
                    request.RuntimeContext);
            }

            var observation = request.Observation;
            RedactObservation(observation);

            var safeRequest = new SmartSummaryRequest
            {
                RequestId = request.RequestId,
                Observation = observation,
                ObservationJson = JsonSerializer.Serialize(observation),
                RuntimeContext = request.RuntimeContext
            };

            if (!AIGuideSecretRedactor.ContainsSecretPattern(observation.Application))
            {
                AppLogger.Debug(
                    "Smart summary generation starting.",
                    category: "AIGuide",
                    action: "SummaryStart");
            }

            var options = new GuidanceProviderOptions
            {
                Provider = settings.AiProvider,
                ApiKey = apiKey,
                Endpoint = settings.VisionEndpoint,
                Model = string.IsNullOrWhiteSpace(settings.AiModel) ? "gpt-4o-mini" : settings.AiModel,
                TimeoutSeconds = settings.VisionTimeoutSeconds
            };

            return await _provider.GenerateAsync(safeRequest, options, cancellationToken).ConfigureAwait(false);
        }

        private static void RedactObservation(VisionObservation observation)
        {
            observation.DetectedSoftware ??= new List<string>();
            observation.VisibleControls ??= new List<VisibleControl>();
            observation.VisibleText ??= new List<VisibleTextBlock>();
            observation.VisibleErrors ??= new List<VisibleError>();
            observation.VisibleWarnings ??= new List<VisibleError>();
            observation.Limitations ??= new List<string>();

            observation.WindowTitle = AIGuideSecretRedactor.Redact(observation.WindowTitle);
            observation.Application = AIGuideSecretRedactor.Redact(observation.Application);
            observation.CurrentTaskHint = AIGuideSecretRedactor.Redact(observation.CurrentTaskHint);

            foreach (var block in observation.VisibleText)
                block.Text = AIGuideSecretRedactor.Redact(block.Text);

            foreach (var control in observation.VisibleControls)
                control.Label = AIGuideSecretRedactor.Redact(control.Label);
        }
    }
}
