using System.Text;
using System.Text.RegularExpressions;
using Netvium.Helpers;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Extracts structured visual context from a fresh capture before vision LLM analysis.
    /// </summary>
    internal static class ScreenVisualContextExtractor
    {
        private static readonly Regex EventIdPattern = new(
            @"(?:event\s*id|olay\s*kimliği|olay\s*kimligi)\s*[:\s]?\s*(\d{3,5})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex StandaloneEventIdPattern = new(
            @"\b1074\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static async Task<ScreenVisualContext> ExtractAsync(
            ScreenContext screen,
            string? previousFingerprint,
            CancellationToken ct = default)
        {
            if (!screen.HasImage || screen.ImageBytes == null)
            {
                return Empty(previousFingerprint, applicationChanged: false);
            }

            var w = screen.Width;
            var h = screen.Height;
            var bytes = screen.ImageBytes;

            var titleTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.02, 0.0, 0.96, 0.07, ct);
            var statusTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.02, 0.93, 0.96, 0.06, ct);
            var treeTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.0, 0.08, 0.26, 0.82, ct);
            var listTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.24, 0.12, 0.72, 0.78, ct);
            var dialogTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.18, 0.08, 0.64, 0.18, ct);
            var modalCenterTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.30, 0.25, 0.40, 0.50, ct);
            var detailTask = ScreenOcrEngine.RecognizeRegionAsync(bytes, w, h, 0.24, 0.55, 0.72, 0.35, ct);

            await Task.WhenAll(titleTask, statusTask, treeTask, listTask, dialogTask, modalCenterTask, detailTask).ConfigureAwait(false);

            var titleOcr = titleTask.Result;
            var statusOcr = statusTask.Result;
            var treeOcr = treeTask.Result;
            var listOcr = listTask.Result;
            var dialogOcr = dialogTask.Result;
            var modalCenterOcr = modalCenterTask.Result;
            var detailOcr = detailTask.Result;

            var modalDetection = ModalDialogDetector.Detect(
                dialogOcr.Text,
                modalCenterOcr.Text,
                titleOcr.Text);

            var combined = string.Join("\n",
                titleOcr.Text,
                treeOcr.Text,
                listOcr.Text,
                detailOcr.Text,
                statusOcr.Text,
                dialogOcr.Text,
                modalCenterOcr.Text);

            var (mmcConsole, snapIn, consoleType) = MmcConsoleDetector.Detect(combined);
            if (mmcConsole == MmcConsoleKind.Unknown)
            {
                var explorerFromTitle = DetectFileExplorer(titleOcr.Text, listOcr.Text);
                if (explorerFromTitle)
                {
                    mmcConsole = MmcConsoleKind.FileExplorer;
                    snapIn = MmcConsoleDetector.SnapInDisplayName(mmcConsole);
                    consoleType = "Shell";
                }
            }

            if (modalDetection.IsSecurityAlert)
            {
                mmcConsole = MmcConsoleKind.EndpointSecurity;
                snapIn = MmcConsoleDetector.SnapInDisplayName(mmcConsole);
                consoleType = "Security";
            }
            if (mmcConsole == MmcConsoleKind.Unknown && !string.IsNullOrWhiteSpace(titleOcr.Text))
            {
                var fromTitle = MmcConsoleDetector.Detect(titleOcr.Text);
                mmcConsole = fromTitle.Console;
                snapIn = fromTitle.SnapInName;
                consoleType = fromTitle.ConsoleType;
            }

            var activeWindowTitle = PickBestTitle(titleOcr.Text, snapIn);
            var selectedTreeNode = ExtractSelectedTreeNode(treeOcr.Text);
            var selectedListItem = ExtractSelectedListRow(listOcr.Text);
            var dialogTitle = modalDetection.HasModal && !string.IsNullOrWhiteSpace(modalDetection.ModalTitle)
                ? modalDetection.ModalTitle
                : ExtractDialogTitle(dialogOcr.Text, titleOcr.Text);
            var eventId = DetectEventId(combined, listOcr.Text, detailOcr.Text);
            var eventSummary = ExtractEventDescription(detailOcr.Text, listOcr.Text, eventId);

            var ocrConfidence = AverageConfidence(
                titleOcr.Confidence,
                statusOcr.Confidence,
                listOcr.Confidence,
                treeOcr.Confidence);

            var limitations = new List<string>();
            if (ocrConfidence < 0.45)
                limitations.Add("OCR confidence is low; visible text may be incomplete.");
            if (string.IsNullOrWhiteSpace(titleOcr.Text))
                limitations.Add("Window title could not be read from the capture.");

            var fingerprint = ApplicationContextGuard.BuildFingerprint(new ScreenVisualContext
            {
                ActiveWindowTitle = activeWindowTitle,
                MmcSnapIn = snapIn,
                ConsoleType = string.IsNullOrWhiteSpace(consoleType) ? InferConsoleType(mmcConsole) : consoleType,
                MmcConsole = mmcConsole
            });

            var applicationChanged = ApplicationContextGuard.HasApplicationChanged(previousFingerprint, fingerprint);

            var treeAnalysis = NavigationTreeAnalyzer.Analyze(treeOcr.Text, ocrConfidence);
            var detectedControls = VisibleControlDetector.Detect(
                new ScreenVisualContext { OcrTreeText = treeOcr.Text, SelectedTreeNode = selectedTreeNode },
                null);

            return new ScreenVisualContext
            {
                ActiveWindowTitle = activeWindowTitle,
                OcrWindowTitle = titleOcr.Text,
                MmcSnapIn = snapIn,
                ConsoleType = string.IsNullOrWhiteSpace(consoleType) ? InferConsoleType(mmcConsole) : consoleType,
                MmcConsole = mmcConsole,
                SelectedTreeNode = selectedTreeNode,
                SelectedListItem = selectedListItem,
                OcrSelectedRow = listOcr.Text,
                VisibleDialogTitle = dialogTitle,
                StatusBarText = statusOcr.Text,
                OcrStatusBar = statusOcr.Text,
                DetectedEventId = eventId,
                EventDescriptionSummary = eventSummary,
                OcrConfidence = ocrConfidence,
                OcrLimitations = limitations,
                ApplicationFingerprint = fingerprint,
                ApplicationChanged = applicationChanged,
                HasModalDialog = modalDetection.HasModal,
                ModalTitle = modalDetection.ModalTitle,
                ModalBodyText = modalDetection.ModalBodyText,
                ModalPriority = modalDetection.HasModalPriority,
                FocusedContext = modalDetection.FocusedContext,
                OcrTreeText = treeOcr.Text,
                NavigationTree = treeAnalysis.FlatNodes,
                NavigationTreeSummary = treeAnalysis.Summary,
                DetectedControls = detectedControls
            };
        }

        private static ScreenVisualContext Empty(string? previousFingerprint, bool applicationChanged) =>
            new()
            {
                ApplicationFingerprint = previousFingerprint ?? string.Empty,
                ApplicationChanged = applicationChanged,
                OcrConfidence = 0,
                OcrLimitations = ["No image available for OCR."]
            };

        private static string PickBestTitle(string titleOcr, string snapIn)
        {
            if (!string.IsNullOrWhiteSpace(titleOcr))
                return titleOcr.Trim();
            return snapIn;
        }

        private static string InferConsoleType(MmcConsoleKind kind) => kind switch
        {
            MmcConsoleKind.FileExplorer => "Shell",
            MmcConsoleKind.Unknown => string.Empty,
            _ => "MMC"
        };

        private static bool DetectFileExplorer(string titleText, string listText)
        {
            var haystack = (titleText + " " + listText).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(haystack))
                return false;

            return haystack.Contains("file explorer", StringComparison.Ordinal)
                   || haystack.Contains("dosya gezgini", StringComparison.Ordinal)
                   || haystack.Contains("this pc", StringComparison.Ordinal)
                   || haystack.Contains("bu bilgisayar", StringComparison.Ordinal)
                   || (haystack.Contains("explorer", StringComparison.Ordinal)
                       && (haystack.Contains(":\\", StringComparison.Ordinal)
                           || haystack.Contains("address", StringComparison.Ordinal)));
        }

        private static string ExtractSelectedTreeNode(string treeText)
        {
            if (string.IsNullOrWhiteSpace(treeText))
                return string.Empty;

            var lines = treeText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length >= 2)
                .ToList();

            if (lines.Count == 0)
                return string.Empty;

            return lines[^1];
        }

        private static string ExtractSelectedListRow(string listText)
        {
            if (string.IsNullOrWhiteSpace(listText))
                return string.Empty;

            var lines = listText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length >= 3 && !IsLikelyHeaderRow(l))
                .ToList();

            foreach (var line in lines)
            {
                if (StandaloneEventIdPattern.IsMatch(line) || EventIdPattern.IsMatch(line))
                    return line;
            }

            return lines.Count > 0 ? lines[0] : string.Empty;
        }

        private static bool IsLikelyHeaderRow(string line)
        {
            var lower = line.ToLowerInvariant();
            return lower.Contains("level", StringComparison.Ordinal)
                   && lower.Contains("date", StringComparison.Ordinal)
                   || lower.Contains("seviye", StringComparison.Ordinal)
                   && lower.Contains("tarih", StringComparison.Ordinal);
        }

        private static string ExtractDialogTitle(string dialogText, string titleText)
        {
            if (string.IsNullOrWhiteSpace(dialogText))
                return string.Empty;

            var firstLine = dialogText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.Length >= 3);

            if (string.IsNullOrWhiteSpace(firstLine))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(titleText)
                && firstLine.Equals(titleText.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return firstLine;
        }

        private static string? DetectEventId(string combined, string listText, string detailText)
        {
            foreach (var source in new[] { listText, detailText, combined })
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                var match = EventIdPattern.Match(source);
                if (match.Success)
                    return match.Groups[1].Value;

                if (StandaloneEventIdPattern.IsMatch(source))
                    return "1074";
            }

            return null;
        }

        private static string? ExtractEventDescription(string detailText, string listText, string? eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                return null;

            var source = !string.IsNullOrWhiteSpace(detailText) ? detailText : listText;
            if (string.IsNullOrWhiteSpace(source))
                return null;

            var lines = source
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length > 8)
                .ToList();

            if (lines.Count == 0)
                return null;

            var sb = new StringBuilder();
            foreach (var line in lines.Take(4))
            {
                if (EventIdPattern.IsMatch(line) && line.Length < 24)
                    continue;

                sb.AppendLine(line);
            }

            var summary = sb.ToString().Trim();
            return summary.Length >= 8 ? summary : null;
        }

        private static double AverageConfidence(params double[] values)
        {
            if (values.Length == 0)
                return 0;

            return values.Where(v => v > 0).DefaultIfEmpty(0).Average();
        }
    }
}
