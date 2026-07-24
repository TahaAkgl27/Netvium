using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class TimelineEngine
    {
        public ObservationTimeline Update(
            VisualMentorInput input,
            ObservationResult observations,
            ScreenChangeInfo screenChange,
            WorkflowState? workflow,
            bool summaryGenerated)
        {
            var timeline = input.PriorTimeline != null
                ? new ObservationTimeline { Entries = input.PriorTimeline.Entries.ToList() }
                : new ObservationTimeline();

            Append(timeline, TimelineEventType.ScreenAnalyzed, AIGuideLocalizedCatalogs.TimelineScreenAnalyzed());

            if (workflow?.Status is WorkflowStatus.Detected or WorkflowStatus.InProgress or WorkflowStatus.Completed)
            {
                Append(timeline, TimelineEventType.WorkflowDetected,
                    AIGuideLocalizedCatalogs.TimelineWorkflowDetected(
                        AIGuideLanguage.LocalizeWorkflowName(workflow.Workflow?.Id, workflow.Workflow?.Name)));
            }

            if (screenChange.ApplicationChanged)
            {
                Append(timeline, TimelineEventType.ApplicationChanged,
                    AIGuideLocalizedCatalogs.TimelineApplicationChanged(
                        screenChange.PreviousApplication ?? "—",
                        screenChange.CurrentApplication ?? "—"));
            }

            if (input.RuntimeContext?.DetectedRole.Role != DetectedServerRoleKind.Unknown)
            {
                Append(timeline, TimelineEventType.RoleDetected,
                    AIGuideLocalizedCatalogs.TimelineRoleDetected(input.RuntimeContext!.DetectedRole.DisplayName));
            }

            if (summaryGenerated)
                Append(timeline, TimelineEventType.SummaryGenerated, AIGuideLocalizedCatalogs.TimelineSummaryGenerated());

            if (screenChange.DialogOpened)
                Append(timeline, TimelineEventType.DialogOpened, AIGuideLocalizedCatalogs.TimelineDialogOpened());

            if (screenChange.DialogClosed)
                Append(timeline, TimelineEventType.DialogClosed, AIGuideLocalizedCatalogs.TimelineDialogClosed());

            if (screenChange.SelectionChanged)
                Append(timeline, TimelineEventType.SelectionChanged, AIGuideLocalizedCatalogs.TimelineSelectionChanged());

            if (screenChange.WindowTitleChanged)
                Append(timeline, TimelineEventType.WindowChanged, AIGuideLocalizedCatalogs.TimelineWindowChanged());

            foreach (var turn in input.ConversationHistory.TakeLast(2))
            {
                if (turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    Append(timeline, TimelineEventType.UserQuestion, AIGuideLocalizedCatalogs.TimelineUserQuestion());
                else if (turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    Append(timeline, TimelineEventType.AnswerGenerated, AIGuideLocalizedCatalogs.TimelineAnswerGenerated());
            }

            Trim(timeline, 30);
            return timeline;
        }

        public void AppendUserQuestion(ObservationTimeline timeline, string question)
        {
            Append(timeline, TimelineEventType.UserQuestion, AIGuideLocalizedCatalogs.TimelineUserQuestion());
            Trim(timeline, 30);
        }

        public void AppendAnswer(ObservationTimeline timeline)
        {
            Append(timeline, TimelineEventType.AnswerGenerated, AIGuideLocalizedCatalogs.TimelineAnswerGenerated());
            Trim(timeline, 30);
        }

        private static void Append(ObservationTimeline timeline, TimelineEventType type, string message)
        {
            var last = timeline.Entries.LastOrDefault();
            if (last != null
                && last.EventType == type
                && last.Message == message
                && (DateTime.UtcNow - last.TimestampUtc).TotalSeconds < 2)
                return;

            timeline.Entries.Add(new TimelineEntry
            {
                EventType = type,
                Message = message,
                TimestampUtc = DateTime.UtcNow
            });
        }

        private static void Trim(ObservationTimeline timeline, int max)
        {
            if (timeline.Entries.Count > max)
                timeline.Entries.RemoveRange(0, timeline.Entries.Count - max);
        }
    }
}
