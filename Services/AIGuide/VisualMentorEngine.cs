using System.Text;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Orchestrates proactive mentor content from cached analysis — no screenshots, no automation.
    /// </summary>
    public sealed class VisualMentorEngine
    {
        private readonly ObservationEngine _observations = new();
        private readonly LearningTopicEngine _learning = new();
        private readonly QuestionSuggestionEngine _questions = new();
        private readonly TimelineEngine _timeline = new();

        public VisualMentorDashboard Build(VisualMentorInput input, bool summaryGenerated = true)
        {
            var screenChange = ScreenChangeIntelligence.Analyze(
                input.PreviousObservation,
                input.Observation,
                input.WorkflowState);

            var observationResult = _observations.Generate(input);
            var learning = _learning.Suggest(input);
            var questions = _questions.Suggest(input, observationResult);
            var warnings = BuildWarnings(input, screenChange);
            var currentScreen = BuildCurrentScreenSummary(input.Observation);
            var evidence = BuildEvidence(input);
            var workflowTimeline = BuildWorkflowTimeline(input.WorkflowState);
            var timeline = _timeline.Update(input, observationResult, screenChange, input.WorkflowState, summaryGenerated);

            var confidence = input.RuntimeContext?.Confidence.OverallGuidanceConfidence
                             ?? input.Observation.Confidence;
            var confidenceLabel = AIGuideLanguage.LocalizeConfidenceLabel(
                input.RuntimeContext?.Confidence.OverallGuidanceConfidence
                ?? input.Observation.Confidence);

            var isLowConfidence = SmartSummaryFallbackBuilder.IsLowConfidence(
                input.Observation,
                input.RuntimeContext);

            return new VisualMentorDashboard
            {
                IsReady = !isLowConfidence,
                StatusBadge = isLowConfidence ? AIGuideLanguage.NeedsFocus : AIGuideLanguage.ScreenReady,
                ServerName = input.RuntimeContext?.Server.ServerName,
                Role = input.RuntimeContext?.DetectedRole.DisplayName,
                Application = FirstNonEmpty(
                    input.Observation.Application,
                    input.Observation.WindowTitle),
                WorkflowName = AIGuideLanguage.LocalizeWorkflowName(
                    input.WorkflowState?.Workflow?.Id,
                    input.WorkflowState?.Workflow?.Name),
                ConfidenceLabel = confidenceLabel,
                ConfidenceScore = confidence,
                CurrentScreen = currentScreen,
                Observations = observationResult,
                Workflow = input.WorkflowState,
                WorkflowTimelineSteps = workflowTimeline,
                SuggestedQuestions = questions,
                LearningTopics = learning,
                Warnings = warnings,
                Evidence = evidence,
                Timeline = timeline,
                ScreenChange = screenChange,
                Reminder = AIGuideLanguage.Reminder
            };
        }

        public TimelineEngine Timeline => _timeline;

        private static CurrentScreenSummary BuildCurrentScreenSummary(VisionObservation obs)
        {
            obs.VisibleControls ??= new List<VisibleControl>();
            obs.VisibleText ??= new List<VisibleTextBlock>();
            obs.VisibleErrors ??= new List<VisibleError>();
            obs.VisibleWarnings ??= new List<VisibleError>();

            var dialogs = obs.VisibleControls
                .Where(c => IsDialogLike(c.Label) || IsDialogLike(c.Type))
                .Select(c => c.Label)
                .Concat(obs.VisibleText.Where(t => IsDialogLike(t.Text)).Select(t => t.Text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            var selected = obs.VisibleControls
                .Concat(obs.VisibleText.Select(t => new VisibleControl { Label = t.Text }))
                .Where(c => c.Label.Contains("selected", StringComparison.OrdinalIgnoreCase)
                            || obs.WindowTitle.Contains("properties", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Label)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(4)
                .ToList();

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(obs.WindowTitle))
                sb.AppendLine(AIGuideLocalizedCatalogs.CurrentWindow(obs.WindowTitle.Trim()));
            if (selected.Count > 0)
                sb.AppendLine(AIGuideLocalizedCatalogs.SelectedObjects(string.Join(", ", selected)));
            if (dialogs.Count > 0)
                sb.AppendLine(AIGuideLocalizedCatalogs.Dialogs(string.Join(", ", dialogs)));
            if (obs.VisibleErrors.Count > 0)
                sb.AppendLine(AIGuideLocalizedCatalogs.VisibleErrorsCount(obs.VisibleErrors.Count));
            if (obs.VisibleWarnings.Count > 0)
                sb.AppendLine(AIGuideLocalizedCatalogs.VisibleWarningsCount(obs.VisibleWarnings.Count));

            return new CurrentScreenSummary
            {
                WindowTitle = obs.WindowTitle,
                Application = obs.Application,
                SelectedObjects = selected,
                Dialogs = dialogs,
                VisibleErrors = obs.VisibleErrors.Select(e => e.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Take(4).ToList(),
                VisibleWarnings = obs.VisibleWarnings.Select(w => w.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Take(4).ToList(),
                Narrative = sb.ToString().Trim()
            };
        }

        private static List<MentorWarning> BuildWarnings(VisualMentorInput input, ScreenChangeInfo screenChange)
        {
            var warnings = new List<MentorWarning>();
            var metrics = input.RuntimeContext?.Metrics;

            if (metrics?.IsAvailable == true && metrics.OnlineStatus == "Offline")
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningServerOffline(), Severity = ObservationSeverity.Critical });

            if (metrics?.CpuPercent is >= 95)
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningHighCpu(), Severity = ObservationSeverity.Warning });

            if (metrics?.DiskPercent is >= 90)
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningLowDisk(), Severity = ObservationSeverity.Warning });

            if (metrics?.IsAvailable != true)
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningNoTelemetry(), Severity = ObservationSeverity.Notice });

            if (input.Observation.Confidence < 0.45)
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningLowVisionConfidence(), Severity = ObservationSeverity.Warning });

            if (input.Observation.Limitations.Any(l =>
                    l.Contains("partial", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("blank", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("rdp", StringComparison.OrdinalIgnoreCase)))
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningCaptureLimited(), Severity = ObservationSeverity.Warning });

            if (screenChange.ApplicationChanged && screenChange.WorkflowContinued == false)
                warnings.Add(new MentorWarning { Message = AIGuideLocalizedCatalogs.WarningApplicationChanged(), Severity = ObservationSeverity.Notice });

            return warnings;
        }

        private static List<MentorEvidenceItem> BuildEvidence(VisualMentorInput input)
        {
            var evidence = new List<MentorEvidenceItem>();
            var obs = input.Observation;

            foreach (var control in obs.VisibleControls.Take(6))
            {
                if (string.IsNullOrWhiteSpace(control.Label))
                    continue;
                evidence.Add(new MentorEvidenceItem { Label = AIGuideLanguage.LocalizeEvidenceLabel("Control"), Detail = $"{control.Type}: {control.Label}" });
            }

            foreach (var text in obs.VisibleText.Take(4))
            {
                if (string.IsNullOrWhiteSpace(text.Text))
                    continue;
                evidence.Add(new MentorEvidenceItem { Label = AIGuideLanguage.LocalizeEvidenceLabel("Text"), Detail = text.Text.Length > 80 ? text.Text[..80] + "…" : text.Text });
            }

            foreach (var err in obs.VisibleErrors.Take(3))
                evidence.Add(new MentorEvidenceItem { Label = AIGuideLanguage.LocalizeEvidenceLabel("Error"), Detail = err.Message });

            foreach (var warn in obs.VisibleWarnings.Take(3))
                evidence.Add(new MentorEvidenceItem { Label = AIGuideLanguage.LocalizeEvidenceLabel("Warning"), Detail = warn.Message });

            if (input.RuntimeContext != null)
            {
                evidence.Add(new MentorEvidenceItem
                {
                    Label = AIGuideLanguage.LocalizeEvidenceLabel("Runtime context"),
                    Detail = AIGuideLocalizedCatalogs.RuntimeContextDetail(
                        input.RuntimeContext.Server.ServerName ?? "—",
                        input.RuntimeContext.DetectedRole.DisplayName,
                        input.RuntimeContext.Server.ConnectionType)
                });
                evidence.Add(new MentorEvidenceItem
                {
                    Label = AIGuideLanguage.LocalizeEvidenceLabel("Confidence"),
                    Detail = AIGuideLocalizedCatalogs.ConfidenceDetail(
                        AIGuideLanguage.LocalizeConfidenceLabel(input.RuntimeContext.Confidence.VisionLabel),
                        AIGuideLanguage.LocalizeConfidenceLabel(input.RuntimeContext.Confidence.ContextLabel),
                        AIGuideLanguage.LocalizeConfidenceLabel(input.RuntimeContext.Confidence.OverallLabel))
                });
            }

            return evidence;
        }

        private static List<string> BuildWorkflowTimeline(WorkflowState? workflow)
        {
            if (workflow?.Workflow == null)
                return new List<string>();

            return workflow.Workflow.Steps
                .Select(s => AIGuideLanguage.LocalizeWorkflowStepName(workflow.Workflow?.Id, s.Id, s.Name))
                .ToList();
        }

        private static bool IsDialogLike(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Contains("dialog", StringComparison.OrdinalIgnoreCase)
                || value.Contains("properties", StringComparison.OrdinalIgnoreCase)
                || value.Contains("reset password", StringComparison.OrdinalIgnoreCase)
                || value.Contains("wizard", StringComparison.OrdinalIgnoreCase));

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }

            return null;
        }
    }
}
