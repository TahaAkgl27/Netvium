using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Read-only workflow detection from visible screen evidence. Never controls the computer.
    /// </summary>
    public sealed class WorkflowDetectionEngine
    {
        private const double MinimumConfidence = 0.35;
        private const double StepMatchWeight = 0.55;
        private const double ConversationBoost = 0.15;
        private const double ContinuationBoost = 0.12;

        public WorkflowState Detect(WorkflowDetectionInput input)
        {
            var current = input.CurrentObservation;
            var comparison = ScreenComparisonService.Compare(input.PreviousObservation, current);
            var history = CloneHistory(input.PriorHistory);

            if (current.Confidence < 0.35
                || SmartSummaryFallbackBuilder.IsLowConfidence(current, input.RuntimeContext))
            {
                return WorkflowState.Unknown(
                    "The screen could not be clearly identified for workflow detection.");
            }

            if (comparison.ApplicationChanged)
            {
                history = new WorkflowHistory();
            }

            var candidates = ScoreWorkflows(input, comparison);
            if (candidates.Count == 0)
            {
                return BuildUnknownState(comparison, history,
                    "No supported workflow matched the visible evidence.");
            }

            var best = candidates.OrderByDescending(c => c.Score).First();
            if (best.Score < MinimumConfidence)
            {
                return BuildUnknownState(comparison, history,
                    "Insufficient evidence to identify a workflow.");
            }

            var workflow = best.Workflow;
            var stepIndex = Math.Clamp(best.StepIndex, 1, workflow.Steps.Count);
            var step = workflow.Steps[stepIndex - 1];

            var confidence = Math.Clamp(best.Score, 0, 1);
            var continued = DetermineWorkflowContinued(input, comparison, workflow, stepIndex);

            if (comparison.ApplicationChanged)
                continued = false;

            var progress = BuildProgress(workflow, stepIndex, step);
            var evidence = BuildEvidence(current, step, best.MatchedPatterns);
            var nextHint = BuildNextStepExplanation(step, progress, continued);

            AppendHistory(history, current, workflow.Name, step.Name);

            var status = stepIndex >= workflow.Steps.Count
                ? WorkflowStatus.Completed
                : stepIndex > 1 || continued == true
                    ? WorkflowStatus.InProgress
                    : WorkflowStatus.Detected;

            return new WorkflowState
            {
                Status = status,
                Workflow = workflow,
                Progress = progress,
                Confidence = confidence,
                ConfidenceLabel = ContextConfidence.ToLabel(confidence),
                PreviousApplication = comparison.PreviousApplication,
                CurrentApplication = comparison.CurrentApplication ?? current.Application,
                ApplicationChanged = comparison.ApplicationChanged,
                WorkflowContinued = continued,
                Evidence = evidence,
                NextLikelyStepExplanation = nextHint,
                History = history,
                StatusMessage = status switch
                {
                    WorkflowStatus.Completed => "Workflow appears complete based on visible evidence.",
                    WorkflowStatus.InProgress => "Workflow in progress.",
                    WorkflowStatus.Detected => "Workflow detected.",
                    _ => "Workflow status updated."
                }
            };
        }

        private static List<WorkflowCandidate> ScoreWorkflows(
            WorkflowDetectionInput input,
            ScreenComparisonResult comparison)
        {
            var haystack = BuildEvidenceHaystack(input.CurrentObservation);
            var conversationHaystack = string.Join(" ",
                input.ConversationHistory.Select(t => t.Content)).ToLowerInvariant();

            var results = new List<WorkflowCandidate>();

            foreach (var workflow in WorkflowCatalog.All)
            {
                if (!MatchesApplication(workflow, haystack, comparison.CurrentApplication))
                    continue;

                for (var i = workflow.Steps.Count; i >= 1; i--)
                {
                    var step = workflow.Steps[i - 1];
                    var matched = MatchPatterns(haystack, step.EvidencePatterns);
                    if (matched.Count == 0)
                        continue;

                    var patternScore = (double)matched.Count / Math.Max(step.EvidencePatterns.Count, 1);
                    var stepScore = patternScore * StepMatchWeight;
                    stepScore += input.CurrentObservation.Confidence * 0.25;

                    if (ConversationMentionsWorkflow(conversationHaystack, workflow))
                        stepScore += ConversationBoost;

                    if (input.ActiveWorkflow?.Id == workflow.Id && i >= input.ActiveStepIndex)
                        stepScore += ContinuationBoost;

                    if (input.PreviousObservation != null && i > 1)
                    {
                        var prevHaystack = BuildEvidenceHaystack(input.PreviousObservation);
                        var prevStep = workflow.Steps[i - 2];
                        if (MatchPatterns(prevHaystack, prevStep.EvidencePatterns).Count > 0)
                            stepScore += ContinuationBoost;
                    }

                    results.Add(new WorkflowCandidate
                    {
                        Workflow = workflow,
                        StepIndex = i,
                        Score = Math.Clamp(stepScore, 0, 1),
                        MatchedPatterns = matched
                    });
                }
            }

            return results;
        }

        private static bool MatchesApplication(
            DetectedWorkflow workflow,
            string haystack,
            string? currentApplication)
        {
            if (workflow.ApplicationPatterns.Any(p => haystack.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (string.IsNullOrWhiteSpace(currentApplication))
                return false;

            var app = currentApplication.ToLowerInvariant();
            return workflow.ApplicationPatterns.Any(p => app.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ConversationMentionsWorkflow(string conversation, DetectedWorkflow workflow)
        {
            if (string.IsNullOrWhiteSpace(conversation))
                return false;

            var keywords = workflow.Name.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .Concat(workflow.Steps.SelectMany(s => s.EvidencePatterns))
                .Distinct();

            return keywords.Any(k => conversation.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static bool? DetermineWorkflowContinued(
            WorkflowDetectionInput input,
            ScreenComparisonResult comparison,
            DetectedWorkflow workflow,
            int stepIndex)
        {
            if (input.PreviousObservation == null)
                return null;

            if (comparison.ApplicationChanged)
                return false;

            if (input.ActiveWorkflow?.Id == workflow.Id && stepIndex >= input.ActiveStepIndex)
                return true;

            if (comparison.WindowTitleChanged && comparison.SameApplicationFamily)
                return true;

            return null;
        }

        private static WorkflowProgress BuildProgress(DetectedWorkflow workflow, int stepIndex, WorkflowStep step)
        {
            var total = workflow.Steps.Count;
            var percent = total > 0 ? (double)stepIndex / total * 100 : 0;

            return new WorkflowProgress
            {
                CurrentStepIndex = stepIndex,
                TotalSteps = total,
                CurrentStepName = step.Name,
                NextLikelyStepHint = step.NextLikelyStepHint,
                ProgressPercent = Math.Clamp(percent, 0, 100)
            };
        }

        private static string? BuildNextStepExplanation(
            WorkflowStep step,
            WorkflowProgress progress,
            bool? continued)
        {
            if (!string.IsNullOrWhiteSpace(step.NextLikelyStepHint))
                return step.NextLikelyStepHint;

            if (progress.CurrentStepIndex >= progress.TotalSteps)
                return "The visible evidence suggests the workflow may be near completion.";

            return continued == false
                ? "The application changed. A new workflow context may have started."
                : null;
        }

        private static List<WorkflowEvidence> BuildEvidence(
            VisionObservation observation,
            WorkflowStep step,
            IReadOnlyList<string> matchedPatterns)
        {
            var evidence = new List<WorkflowEvidence>
            {
                new() { Label = "Current step", Detail = step.Name }
            };

            if (!string.IsNullOrWhiteSpace(observation.Application))
            {
                evidence.Add(new WorkflowEvidence
                {
                    Label = "Application",
                    Detail = observation.Application
                });
            }

            if (!string.IsNullOrWhiteSpace(observation.WindowTitle))
            {
                evidence.Add(new WorkflowEvidence
                {
                    Label = "Window",
                    Detail = observation.WindowTitle
                });
            }

            foreach (var pattern in matchedPatterns.Take(4))
            {
                evidence.Add(new WorkflowEvidence
                {
                    Label = "Matched evidence",
                    Detail = pattern
                });
            }

            return evidence;
        }

        private static WorkflowState BuildUnknownState(
            ScreenComparisonResult comparison,
            WorkflowHistory history,
            string message)
        {
            return new WorkflowState
            {
                Status = WorkflowStatus.Unknown,
                StatusMessage = message,
                Confidence = 0,
                ConfidenceLabel = "Low",
                PreviousApplication = comparison.PreviousApplication,
                CurrentApplication = comparison.CurrentApplication,
                ApplicationChanged = comparison.ApplicationChanged,
                WorkflowContinued = comparison.ApplicationChanged ? false : null,
                History = history
            };
        }

        private static string BuildEvidenceHaystack(VisionObservation observation)
        {
            var parts = new List<string>
            {
                observation.Application,
                observation.WindowTitle,
                observation.ScreenType,
                observation.CurrentTaskHint
            };

            parts.AddRange(observation.DetectedSoftware);
            parts.AddRange(observation.VisibleControls.Select(c => c.Label));
            parts.AddRange(observation.VisibleControls.Select(c => c.Type));
            parts.AddRange(observation.VisibleText.Select(t => t.Text));
            parts.AddRange(observation.Limitations);

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)))
                .ToLowerInvariant();
        }

        private static List<string> MatchPatterns(string haystack, IEnumerable<string> patterns)
        {
            var matched = new List<string>();
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                if (haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    matched.Add(pattern);
            }

            return matched;
        }

        private static WorkflowHistory CloneHistory(WorkflowHistory? history)
        {
            if (history == null)
                return new WorkflowHistory();

            return new WorkflowHistory
            {
                Entries = history.Entries.ToList()
            };
        }

        private static void AppendHistory(
            WorkflowHistory history,
            VisionObservation observation,
            string workflowName,
            string stepName)
        {
            history.Entries.Add(new WorkflowHistoryEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Application = observation.Application,
                WindowTitle = observation.WindowTitle,
                WorkflowName = workflowName,
                StepName = stepName
            });

            if (history.Entries.Count > 20)
                history.Entries.RemoveRange(0, history.Entries.Count - 20);
        }

        private sealed class WorkflowCandidate
        {
            public required DetectedWorkflow Workflow { get; init; }
            public int StepIndex { get; init; }
            public double Score { get; init; }
            public List<string> MatchedPatterns { get; init; } = new();
        }
    }
}
