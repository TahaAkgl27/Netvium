using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class SmartSummaryFallbackBuilder
    {
        public static bool IsLowConfidence(VisionObservation observation, RuntimeContext? runtimeContext)
        {
            if (observation.Confidence < 0.45)
                return true;

            if (observation.Limitations.Any(l =>
                    l.Contains("blank", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("black", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("rdp", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (runtimeContext?.Confidence.VisionLabel == "Low")
                return true;

            return string.IsNullOrWhiteSpace(observation.Application)
                   && string.IsNullOrWhiteSpace(observation.WindowTitle)
                   && observation.VisibleControls.Count == 0
                   && observation.VisibleText.Count == 0;
        }

        public static SmartScreenSummary Build(
            string requestId,
            VisionObservation observation,
            RuntimeContext? runtimeContext)
        {
            var lowConfidence = IsLowConfidence(observation, runtimeContext);
            var application = FirstNonEmpty(
                runtimeContext?.Application.DetectedApplication,
                observation.Application,
                observation.WindowTitle);

            var visibleItems = ExtractVisibleItems(observation);
            var topics = SmartTopicCatalog.Resolve(application, observation.WindowTitle, lowConfidence).ToList();
            var evidence = BuildEvidence(observation, runtimeContext);

            var screenStatus = lowConfidence
                ? AIGuideLocalizedCatalogs.ScreenStatusLowConfidence()
                : AIGuideLocalizedCatalogs.ScreenStatusSuccess();

            var summary = new SmartScreenSummary
            {
                RequestId = requestId,
                Success = true,
                ScreenStatus = screenStatus,
                ServerName = runtimeContext?.Server.ServerName,
                DetectedRole = runtimeContext?.DetectedRole.DisplayName ?? "Unknown",
                Application = application,
                VisibleItems = visibleItems,
                SuggestedTopics = topics,
                Evidence = evidence,
                Confidence = runtimeContext?.Confidence,
                IsLowConfidence = lowConfidence,
                Reminder = AIGuideLanguage.Reminder
            };

            summary.FormattedSummary = SmartSummaryFormatter.Format(summary);
            return summary;
        }

        private static List<string> ExtractVisibleItems(VisionObservation observation)
        {
            var items = new List<string>();

            foreach (var control in observation.VisibleControls)
            {
                var label = AIGuideSecretRedactor.Redact(control.Label);
                if (!string.IsNullOrWhiteSpace(label))
                    items.Add(label.Trim());
            }

            foreach (var block in observation.VisibleText.Take(8))
            {
                var text = AIGuideSecretRedactor.Redact(block.Text);
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 80)
                    items.Add(text.Trim());
            }

            return items.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        }

        private static List<VisibleEvidenceSummary> BuildEvidence(
            VisionObservation observation,
            RuntimeContext? runtimeContext)
        {
            var evidence = new List<VisibleEvidenceSummary>();

            if (!string.IsNullOrWhiteSpace(observation.Application))
            {
                evidence.Add(new VisibleEvidenceSummary
                {
                    Label = AIGuideLanguage.LocalizeEvidenceLabel("Application"),
                    Detail = observation.Application
                });
            }

            if (!string.IsNullOrWhiteSpace(runtimeContext?.Server.ServerName))
            {
                evidence.Add(new VisibleEvidenceSummary
                {
                    Label = AIGuideLanguage.LocalizeEvidenceLabel("Server"),
                    Detail = runtimeContext.Server.ServerName!
                });
            }

            if (runtimeContext?.DetectedRole.Role != DetectedServerRoleKind.Unknown)
            {
                evidence.Add(new VisibleEvidenceSummary
                {
                    Label = AIGuideLanguage.LocalizeEvidenceLabel("Role"),
                    Detail = runtimeContext!.DetectedRole.DisplayName
                });
            }

            foreach (var item in observation.VisibleControls.Take(4))
            {
                if (string.IsNullOrWhiteSpace(item.Label))
                    continue;

                evidence.Add(new VisibleEvidenceSummary
                {
                    Label = AIGuideLanguage.LocalizeEvidenceLabel("Visible control"),
                    Detail = item.Label
                });
            }

            return evidence;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }
    }
}
