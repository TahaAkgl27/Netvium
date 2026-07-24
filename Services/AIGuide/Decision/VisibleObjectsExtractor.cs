using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Extracts on-screen object names from OCR/visual context for hallucination checks.</summary>
    public sealed class VisibleObjectsExtractor
    {
        public IReadOnlyList<string> Extract(ScreenVisualContext? ctx, WorkflowTarget? target = null)
        {
            if (ctx == null)
                return Array.Empty<string>();

            var objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddParts(objects, ctx.ActiveWindowTitle);
            AddParts(objects, ctx.OcrWindowTitle);
            AddParts(objects, ctx.StatusBarText);
            AddParts(objects, ctx.OcrStatusBar);
            AddParts(objects, ctx.SelectedListItem);
            AddParts(objects, ctx.SelectedTreeNode);
            AddParts(objects, ctx.OcrSelectedRow);
            AddParts(objects, ctx.OcrTreeText);

            if (ConsumerAppDetector.TryDetect(ctx.ActiveWindowTitle, out var app))
                objects.Add(app);

            var mmcName = MmcConsoleDetector.SnapInDisplayName(ctx.MmcConsole);
            if (!string.IsNullOrWhiteSpace(mmcName))
                objects.Add(mmcName);

            if (target != null && target.HasUserProvidedPath)
            {
                objects.Add(target.Path);
                var parent = GetParentPath(target.Path);
                if (!string.IsNullOrWhiteSpace(parent))
                    objects.Add(parent);
            }

            return objects.Where(o => o.Length >= 2).ToList();
        }

        private static void AddParts(ISet<string> set, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Length >= 2)
                    set.Add(line.Trim());
            }

            foreach (var part in text.Split(['\\', '/', '>', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length >= 2 && !part.Contains(':'))
                    set.Add(part.Trim());
            }
        }

        private static string GetParentPath(string path)
        {
            var trimmed = path.TrimEnd('\\');
            var idx = trimmed.LastIndexOf('\\');
            return idx > 0 ? trimmed[..idx] : string.Empty;
        }
    }
}
