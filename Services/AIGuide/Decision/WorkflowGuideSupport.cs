using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Shared helpers for workflow guides — reduces duplication.</summary>
    internal static class WorkflowGuideSupport
    {
        public static bool IsTargetVisibleOnScreen(WorkflowTarget target, ScreenVisualContext? ctx)
        {
            if (ctx == null)
                return false;

            var combined = string.Join("\n",
                ctx.ActiveWindowTitle,
                ctx.OcrWindowTitle,
                ctx.StatusBarText,
                ctx.OcrStatusBar,
                ctx.OcrSelectedRow,
                ctx.SelectedListItem,
                ctx.OcrTreeText,
                ctx.SelectedTreeNode);

            if (!string.IsNullOrWhiteSpace(target.Path)
                && combined.Contains(target.Path, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(target.FolderName))
            {
                var selected = ctx.SelectedListItem ?? ctx.OcrSelectedRow ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(selected)
                    && selected.Equals(target.FolderName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (combined.Contains(target.FolderName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(target.Path))
                {
                    var parent = GetParentPath(target.Path);
                    if (!string.IsNullOrWhiteSpace(parent)
                        && combined.Contains(parent, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            var selectedItem = ctx.SelectedListItem ?? ctx.OcrSelectedRow ?? string.Empty;
            if (selectedItem.Contains('.', StringComparison.Ordinal))
                return false;

            if (string.IsNullOrWhiteSpace(target.FolderName)
                && !string.IsNullOrWhiteSpace(selectedItem)
                && ctx.MmcConsole == MmcConsoleKind.FileExplorer
                && combined.Contains(selectedItem, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string GetParentPath(string path)
        {
            var trimmed = path.TrimEnd('\\');
            var idx = trimmed.LastIndexOf('\\');
            return idx > 0 ? trimmed[..idx] : string.Empty;
        }

        public static string ResolveScreenName(ScreenVisualContext? ctx, MmcConsoleKind screenType, string displayName)
        {
            if (screenType == MmcConsoleKind.FileExplorer)
                return "File Explorer";
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
            return ctx?.ActiveWindowTitle ?? "Windows";
        }

        public static WorkflowStepResult Step(string id, int index, string instruction, bool complete = false) =>
            new()
            {
                StepId = id,
                StepIndex = index,
                Instruction = instruction,
                IsComplete = complete
            };

        public static string Localize(bool useTurkish, string en, string tr) =>
            useTurkish ? tr : en;
    }
}
