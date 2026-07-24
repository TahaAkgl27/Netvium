using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Merges OCR-derived context into the vision observation and adjusts confidence.
    /// </summary>
    internal static class ScreenVisualContextEnricher
    {
        public static VisionAnalysisResult Enrich(VisionAnalysisResult result, ScreenVisualContext visualContext)
        {
            if (!result.Success || result.Observation == null)
                return result;

            var observation = result.Observation;

            if (visualContext.MmcConsole != MmcConsoleKind.Unknown)
            {
                var snapIn = MmcConsoleDetector.SnapInDisplayName(visualContext.MmcConsole);
                if (!string.IsNullOrWhiteSpace(snapIn))
                {
                    observation.Application = snapIn;
                    if (!observation.DetectedSoftware.Contains(snapIn, StringComparer.OrdinalIgnoreCase))
                        observation.DetectedSoftware.Add(snapIn);
                }
            }

            if (!string.IsNullOrWhiteSpace(visualContext.ActiveWindowTitle))
                observation.WindowTitle = visualContext.ActiveWindowTitle;
            else if (!string.IsNullOrWhiteSpace(visualContext.OcrWindowTitle))
                observation.WindowTitle = visualContext.OcrWindowTitle;

            if (!string.IsNullOrWhiteSpace(visualContext.ConsoleType))
                observation.ScreenType = visualContext.ConsoleType;

            ApplyEventEvidence(observation, visualContext);
            ApplyVisibleEvidence(observation, visualContext);
            ApplyConfidence(observation, visualContext);

            foreach (var limitation in visualContext.OcrLimitations)
            {
                if (!observation.Limitations.Contains(limitation, StringComparer.OrdinalIgnoreCase))
                    observation.Limitations.Add(limitation);
            }

            observation.CurrentTaskHint = BuildTaskHint(observation, visualContext);

            return result;
        }

        private static void ApplyEventEvidence(VisionObservation observation, ScreenVisualContext visualContext)
        {
            if (visualContext.DetectedEventId != "1074")
                return;

            var eventLabel = "Event ID 1074";
            if (!observation.VisibleText.Any(t =>
                    t.Text.Contains("1074", StringComparison.Ordinal)))
            {
                observation.VisibleText.Add(new VisibleTextBlock
                {
                    Text = eventLabel,
                    Location = "event list or details (OCR)",
                    Confidence = visualContext.OcrConfidence
                });
            }

            if (!string.IsNullOrWhiteSpace(visualContext.EventDescriptionSummary))
            {
                observation.VisibleText.Add(new VisibleTextBlock
                {
                    Text = visualContext.EventDescriptionSummary,
                    Location = "event description (OCR)",
                    Confidence = visualContext.OcrConfidence
                });
            }
        }

        private static void ApplyVisibleEvidence(VisionObservation observation, ScreenVisualContext visualContext)
        {
            AddTextIfPresent(observation, visualContext.SelectedTreeNode, "navigation tree selection (OCR)", visualContext.OcrConfidence);
            AddTextIfPresent(observation, visualContext.SelectedListItem, "selected list row (OCR)", visualContext.OcrConfidence);
            AddTextIfPresent(observation, visualContext.OcrStatusBar, "status bar (OCR)", visualContext.OcrConfidence);
            AddTextIfPresent(observation, visualContext.VisibleDialogTitle, "dialog title (OCR)", visualContext.OcrConfidence);

            if (!string.IsNullOrWhiteSpace(visualContext.NavigationTreeSummary))
            {
                AddTextIfPresent(observation, visualContext.NavigationTreeSummary, "navigation tree hierarchy (OCR)", visualContext.OcrConfidence);
            }

            foreach (var ctrl in visualContext.DetectedControls.Take(8))
            {
                AddTextIfPresent(
                    observation,
                    $"{ctrl.Kind}: {ctrl.Label}",
                    ctrl.Location,
                    visualContext.OcrConfidence);
            }
        }

        private static void AddTextIfPresent(
            VisionObservation observation,
            string text,
            string location,
            double confidence)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (observation.VisibleText.Any(t =>
                    t.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            observation.VisibleText.Add(new VisibleTextBlock
            {
                Text = text,
                Location = location,
                Confidence = confidence
            });
        }

        private static void ApplyConfidence(VisionObservation observation, ScreenVisualContext visualContext)
        {
            var cap = visualContext.OcrConfidence;
            if (visualContext.HasLowOcrConfidence)
                cap = Math.Min(cap, 0.55);

            if (observation.Confidence <= 0)
                observation.Confidence = cap;
            else
                observation.Confidence = Math.Min(observation.Confidence, cap);

            if (visualContext.HasLowOcrConfidence && observation.Confidence > 0.6)
                observation.Confidence = Math.Max(0.35, observation.Confidence - 0.2);
        }

        private static string BuildTaskHint(VisionObservation observation, ScreenVisualContext visualContext)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(observation.Application))
                parts.Add(observation.Application);

            if (!string.IsNullOrWhiteSpace(visualContext.SelectedTreeNode))
                parts.Add($"tree: {visualContext.SelectedTreeNode}");

            if (!string.IsNullOrWhiteSpace(visualContext.NavigationTreeSummary))
                parts.Add($"nav-tree: {Truncate(visualContext.NavigationTreeSummary, 200)}");

            if (!string.IsNullOrWhiteSpace(visualContext.SelectedListItem))
                parts.Add($"selection: {visualContext.SelectedListItem}");

            if (visualContext.DetectedEventId == "1074")
                parts.Add("Event ID 1074 is visible");

            if (!string.IsNullOrWhiteSpace(visualContext.EventDescriptionSummary))
                parts.Add(Truncate(visualContext.EventDescriptionSummary, 120));

            if (parts.Count == 0)
                return observation.CurrentTaskHint;

            return string.Join(" — ", parts);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value.Length <= maxLength)
                return value;

            return value[..(maxLength - 3)] + "...";
        }
    }
}
