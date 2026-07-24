using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class VisibleControlDetector
    {
        public static IReadOnlyList<DetectedVisibleControl> Detect(
            ScreenVisualContext? visualContext,
            VisionObservation? observation)
        {
            var controls = new List<DetectedVisibleControl>();

            if (visualContext == null)
                return controls;

            if (!string.IsNullOrWhiteSpace(visualContext.OcrTreeText) || visualContext.NavigationTree.Count > 0)
            {
                controls.Add(new DetectedVisibleControl
                {
                    Kind = "TreeView",
                    Label = "Left navigation tree",
                    Location = "left panel",
                    IsFocused = !string.IsNullOrWhiteSpace(visualContext.SelectedTreeNode)
                });
            }

            if (!string.IsNullOrWhiteSpace(visualContext.OcrSelectedRow) || !string.IsNullOrWhiteSpace(visualContext.SelectedListItem))
            {
                controls.Add(new DetectedVisibleControl
                {
                    Kind = "ListView",
                    Label = "Main list pane",
                    Location = "right pane",
                    IsFocused = !string.IsNullOrWhiteSpace(visualContext.SelectedListItem)
                });
            }

            if (visualContext.HasModalDialog || visualContext.ModalPriority)
            {
                controls.Add(new DetectedVisibleControl
                {
                    Kind = "ModalDialog",
                    Label = visualContext.ModalTitle,
                    Location = "center overlay",
                    IsFocused = true
                });
            }
            else if (!string.IsNullOrWhiteSpace(visualContext.VisibleDialogTitle))
            {
                controls.Add(new DetectedVisibleControl
                {
                    Kind = "Dialog",
                    Label = visualContext.VisibleDialogTitle,
                    Location = "dialog area"
                });
            }

            if (!string.IsNullOrWhiteSpace(visualContext.OcrStatusBar))
            {
                controls.Add(new DetectedVisibleControl
                {
                    Kind = "StatusBar",
                    Label = visualContext.OcrStatusBar,
                    Location = "bottom status bar"
                });
            }

            if (observation?.VisibleControls != null)
            {
                foreach (var ctrl in observation.VisibleControls)
                {
                    if (string.IsNullOrWhiteSpace(ctrl.Label))
                        continue;

                    if (controls.Any(c => c.Label.Equals(ctrl.Label, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    controls.Add(new DetectedVisibleControl
                    {
                        Kind = ctrl.Type,
                        Label = ctrl.Label,
                        Location = ctrl.Location,
                        IsFocused = ctrl.Label.Contains("selected", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }

            return controls;
        }
    }
}
