using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Builds structured evidence from screen, conversation, knowledge pack, and workflow state.</summary>
    public sealed class EvidenceEngine
    {
        public EvidenceBundle Build(
            MmcConsoleKind screenType,
            string displayName,
            AdminUserIntent intent,
            WorkflowGoal goal,
            WorkflowTarget target,
            ScreenVisualContext? visualContext,
            string userQuestion,
            string workflowName,
            string currentStepId,
            WorkflowScreenState currentState,
            bool targetVisible,
            IReadOnlyList<string> legacyEvidence)
        {
            var visual = BuildVisualEvidence(screenType, displayName, visualContext, target, targetVisible);
            var conversation = BuildConversationEvidence(userQuestion, target, intent);
            var knowledge = BuildKnowledgeEvidence(intent, goal, workflowName);
            var workflow = BuildWorkflowEvidence(workflowName, currentStepId, currentState, legacyEvidence);

            return new EvidenceBundle
            {
                Visual = visual,
                Conversation = conversation,
                Knowledge = knowledge,
                Workflow = workflow
            };
        }

        private static List<EvidenceItem> BuildVisualEvidence(
            MmcConsoleKind screenType,
            string displayName,
            ScreenVisualContext? ctx,
            WorkflowTarget target,
            bool targetVisible)
        {
            var items = new List<EvidenceItem>();

            if (screenType != MmcConsoleKind.Unknown)
                items.Add(Item("Visual", $"{displayName} is open", true));
            else if (!string.IsNullOrWhiteSpace(displayName))
                items.Add(Item("Visual", $"Window: {displayName}", true));

            if (ctx != null)
            {
                if (!string.IsNullOrWhiteSpace(ctx.OcrStatusBar) || !string.IsNullOrWhiteSpace(ctx.StatusBarText))
                {
                    var path = ctx.OcrStatusBar ?? ctx.StatusBarText ?? string.Empty;
                    items.Add(Item("Visual", $"Address bar shows: {path.Trim()}", true));
                }

                var selected = ctx.SelectedListItem ?? ctx.OcrSelectedRow ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(selected))
                    items.Add(Item("Visual", $"Selected item: {selected.Trim()}", true));

                if (ctx.HasModalDialog || ctx.ModalPriority)
                    items.Add(Item("Visual", $"Modal dialog: {ctx.ModalTitle ?? ctx.VisibleDialogTitle ?? "detected"}", true));
            }

            if (!string.IsNullOrWhiteSpace(target.Path))
            {
                items.Add(Item("Visual",
                    targetVisible
                        ? $"Target path {target.Path} appears reachable on screen"
                        : $"Target path {target.Path} is not visible on screen",
                    targetVisible));
            }
            else if (!string.IsNullOrWhiteSpace(target.FolderName))
            {
                items.Add(Item("Visual",
                    targetVisible
                        ? $"Folder {target.FolderName} is visible"
                        : $"Folder {target.FolderName} is not visible",
                    targetVisible));
            }

            return items;
        }

        private static List<EvidenceItem> BuildConversationEvidence(
            string userQuestion,
            WorkflowTarget target,
            AdminUserIntent intent)
        {
            var items = new List<EvidenceItem>();
            if (!string.IsNullOrWhiteSpace(userQuestion))
                items.Add(Item("Conversation", $"User asked: {userQuestion.Trim()}", true));

            if (!string.IsNullOrWhiteSpace(target.UserName))
                items.Add(Item("Conversation", $"Target user: {target.UserName}", true));

            if (!string.IsNullOrWhiteSpace(target.PermissionLevel))
                items.Add(Item("Conversation", $"Requested permission: {target.PermissionLevel}", true));

            if (!string.IsNullOrWhiteSpace(target.Path))
                items.Add(Item("Conversation", $"Target path: {target.Path}", true));

            if (intent != AdminUserIntent.Unknown)
                items.Add(Item("Conversation", $"Detected intent category: {intent}", true));

            return items;
        }

        private static List<EvidenceItem> BuildKnowledgeEvidence(
            AdminUserIntent intent,
            WorkflowGoal goal,
            string workflowName)
        {
            var items = new List<EvidenceItem>();

            if (workflowName != "generic" && workflowName != "clarification")
                items.Add(Item("Knowledge", $"Workflow selected: {workflowName}", true));

            if (goal != WorkflowGoal.Unknown)
                items.Add(Item("Knowledge", $"Goal: {goal}", true));

            if (intent != AdminUserIntent.Unknown)
                items.Add(Item("Knowledge", $"Intent mapped from knowledge pack: {intent}", true));

            return items;
        }

        private static List<EvidenceItem> BuildWorkflowEvidence(
            string workflowName,
            string currentStepId,
            WorkflowScreenState currentState,
            IReadOnlyList<string> legacyEvidence)
        {
            var items = new List<EvidenceItem>();

            if (!string.IsNullOrWhiteSpace(currentStepId))
                items.Add(Item("Workflow", $"Current step: {currentStepId}", true));

            if (currentState != WorkflowScreenState.Unknown)
                items.Add(Item("Workflow", $"Current state: {currentState}", true));

            foreach (var e in legacyEvidence)
            {
                if (!string.IsNullOrWhiteSpace(e))
                    items.Add(Item("Workflow", e, true));
            }

            if (workflowName == "clarification")
                items.Add(Item("Workflow", "Clarification required before continuing", true));

            return items;
        }

        private static EvidenceItem Item(string category, string statement, bool confirmed) =>
            new() { Category = category, Statement = statement, IsConfirmed = confirmed };
    }
}
