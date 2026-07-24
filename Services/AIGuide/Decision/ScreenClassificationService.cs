using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide.Decision
{
    public sealed class ScreenClassificationService
    {
        public (MmcConsoleKind ScreenType, string DisplayName) Classify(ScreenVisualContext? visualContext)
        {
            if (visualContext == null)
                return (MmcConsoleKind.Unknown, string.Empty);

            if (visualContext.MmcConsole != MmcConsoleKind.Unknown)
            {
                var name = MmcConsoleDetector.SnapInDisplayName(visualContext.MmcConsole);
                if (string.IsNullOrWhiteSpace(name))
                    name = visualContext.ActiveWindowTitle;
                return (visualContext.MmcConsole, name);
            }

            var combined = string.Join("\n",
                visualContext.ActiveWindowTitle,
                visualContext.OcrWindowTitle,
                visualContext.OcrSelectedRow);

            if (ConsumerAppDetector.TryDetect(combined, out var consumerLabel))
                return (MmcConsoleKind.Unknown, consumerLabel);

            var detected = MmcConsoleDetector.Detect(combined);
            var display = !string.IsNullOrWhiteSpace(detected.SnapInName)
                ? detected.SnapInName
                : visualContext.ActiveWindowTitle;

            return (detected.Console, display);
        }
    }
}
