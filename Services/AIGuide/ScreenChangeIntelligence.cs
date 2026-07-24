using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ScreenChangeIntelligence
    {
        public static ScreenChangeInfo Analyze(
            VisionObservation? previous,
            VisionObservation current,
            WorkflowState? workflow)
        {
            var comparison = ScreenComparisonService.Compare(previous, current);
            var prevHaystack = BuildHaystack(previous);
            var currHaystack = BuildHaystack(current);

            var prevDialog = HasDialog(prevHaystack);
            var currDialog = HasDialog(currHaystack);

            return new ScreenChangeInfo
            {
                ApplicationChanged = comparison.ApplicationChanged,
                WindowTitleChanged = comparison.WindowTitleChanged,
                DialogOpened = !prevDialog && currDialog,
                DialogClosed = prevDialog && !currDialog,
                SelectionChanged = DetectSelectionChange(previous, current),
                WorkflowContinued = workflow?.WorkflowContinued,
                PreviousApplication = comparison.PreviousApplication,
                CurrentApplication = comparison.CurrentApplication ?? current.Application
            };
        }

        private static bool HasDialog(string haystack) =>
            haystack.Contains("dialog", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("properties", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("reset password", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("wizard", StringComparison.OrdinalIgnoreCase);

        private static bool DetectSelectionChange(VisionObservation? previous, VisionObservation current)
        {
            if (previous == null)
                return false;

            var prevSelected = previous.VisibleControls
                .Concat(previous.VisibleText.Select(t => new VisibleControl { Label = t.Text }))
                .Any(c => c.Label.Contains("selected", StringComparison.OrdinalIgnoreCase));

            var currSelected = current.VisibleControls
                .Concat(current.VisibleText.Select(t => new VisibleControl { Label = t.Text }))
                .Any(c => c.Label.Contains("selected", StringComparison.OrdinalIgnoreCase));

            return prevSelected != currSelected
                   || !string.Equals(previous.WindowTitle, current.WindowTitle, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildHaystack(VisionObservation? obs)
        {
            if (obs == null)
                return string.Empty;

            return string.Join(" ",
                new[] { obs.Application, obs.WindowTitle, obs.ScreenType }
                .Concat(obs.VisibleControls.Select(c => c.Label))
                .Concat(obs.VisibleText.Select(t => t.Text))
                .Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();
        }
    }
}
