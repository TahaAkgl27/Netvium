using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ObservationCatalog
    {
        public static IEnumerable<(Func<VisualMentorInput, bool> Match, Func<VisualMentorInput, ObservationItem> Build)> Rules =>
        [
            (i => i.RuntimeContext?.Metrics.IsAvailable == true && i.RuntimeContext.Metrics.OnlineStatus == "Offline",
                _ => Item(AIGuideLocalizedCatalogs.ObservationServerOffline(), ObservationSeverity.Critical, ObservationCategory.Telemetry, 100)),

            (i => i.RuntimeContext?.Metrics.CpuPercent is >= 95,
                _ => Item(AIGuideLocalizedCatalogs.ObservationHighCpu(), ObservationSeverity.Warning, ObservationCategory.Telemetry, 90)),

            (i => i.RuntimeContext?.Metrics.RamPercent is >= 90,
                _ => Item(AIGuideLocalizedCatalogs.ObservationHighRam(), ObservationSeverity.Warning, ObservationCategory.Telemetry, 85)),

            (i => i.RuntimeContext?.Metrics.DiskPercent is >= 90,
                _ => Item(AIGuideLocalizedCatalogs.ObservationLowDisk(), ObservationSeverity.Warning, ObservationCategory.Telemetry, 88)),

            (i => i.RuntimeContext?.DetectedRole.Role == DetectedServerRoleKind.DomainController,
                _ => Item(AIGuideLocalizedCatalogs.ObservationDomainController(), ObservationSeverity.Notice, ObservationCategory.Role, 80)),

            (i => !string.IsNullOrWhiteSpace(i.RuntimeContext?.Server.ConnectedUsername),
                i => Item(AIGuideLocalizedCatalogs.ObservationUserSession(Placeholder(i.RuntimeContext?.Server.ConnectedUsername)), ObservationSeverity.Info, ObservationCategory.Context, 60)),

            (i => Contains(i, "disabled"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationAccountDisabled(), ObservationSeverity.Notice, ObservationCategory.Vision, 75)),

            (i => Contains(i, "locked"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationAccountLocked(), ObservationSeverity.Notice, ObservationCategory.Vision, 74)),

            (i => i.Observation.VisibleErrors.Count > 0,
                _ => Item(AIGuideLocalizedCatalogs.ObservationVisibleErrors(), ObservationSeverity.Warning, ObservationCategory.Vision, 82)),

            (i => Contains(i, "critical") && ContainsApp(i, "event viewer"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationCriticalEvents(), ObservationSeverity.Warning, ObservationCategory.Vision, 84)),

            (i => Contains(i, "stopped") && ContainsApp(i, "iis"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationIisStopped(), ObservationSeverity.Notice, ObservationCategory.Vision, 70)),

            (i => Contains(i, "off", "stopped", "not running") && ContainsApp(i, "hyper-v"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationVmPoweredOff(), ObservationSeverity.Notice, ObservationCategory.Vision, 72)),

            (i => ContainsApp(i, "dns manager", "dnsmgmt"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationDnsManagerOpen(), ObservationSeverity.Info, ObservationCategory.Application, 55)),

            (i => ContainsApp(i, "active directory", "users and computers"),
                _ => Item(AIGuideLocalizedCatalogs.ObservationAdConsoleOpen(), ObservationSeverity.Info, ObservationCategory.Application, 56)),

            (i => i.WorkflowState?.Status is WorkflowStatus.InProgress or WorkflowStatus.Detected,
                i => Item(AIGuideLocalizedCatalogs.ObservationWorkflowInProgress(
                    AIGuideLanguage.LocalizeWorkflowName(i.WorkflowState?.Workflow?.Id, i.WorkflowState?.Workflow?.Name)), ObservationSeverity.Notice, ObservationCategory.Workflow, 78)),

            (i => i.RuntimeContext?.Metrics.IsAvailable != true,
                _ => Item(AIGuideLocalizedCatalogs.ObservationNoTelemetry(), ObservationSeverity.Notice, ObservationCategory.Telemetry, 65)),

            (i => i.Observation.Confidence < 0.45,
                _ => Item(AIGuideLocalizedCatalogs.ObservationLowVisionConfidence(), ObservationSeverity.Warning, ObservationCategory.Vision, 86)),

            (i => i.Observation.Limitations.Any(l => l.Contains("rdp", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("blank", StringComparison.OrdinalIgnoreCase)),
                _ => Item(AIGuideLocalizedCatalogs.ObservationCaptureLimited(), ObservationSeverity.Warning, ObservationCategory.Vision, 83))
        ];

        private static ObservationItem Item(string text, ObservationSeverity severity, ObservationCategory category, int importance) =>
            new() { Text = text, Severity = severity, Category = category, Importance = importance };

        private static bool Contains(VisualMentorInput input, params string[] terms)
        {
            var haystack = BuildHaystack(input.Observation);
            return terms.Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsApp(VisualMentorInput input, params string[] terms)
        {
            var app = $"{input.Observation.Application} {input.Observation.WindowTitle}".ToLowerInvariant();
            return terms.Any(t => app.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildHaystack(VisionObservation obs) =>
            string.Join(" ",
                new[] { obs.Application, obs.WindowTitle, obs.ScreenType, obs.CurrentTaskHint }
                .Concat(obs.DetectedSoftware)
                .Concat(obs.VisibleControls.Select(c => c.Label))
                .Concat(obs.VisibleText.Select(t => t.Text))
                .Concat(obs.VisibleErrors.Select(e => e.Message))
                .Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();

        private static string Placeholder(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    }
}
