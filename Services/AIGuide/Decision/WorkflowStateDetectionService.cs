using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Infers workflow position from screen OCR and current step.</summary>
    public sealed class WorkflowStateDetectionService
    {
        public WorkflowScreenState DetectCurrentState(
            WorkflowContext context,
            ScreenVisualContext? visualContext,
            bool isClarification,
            bool isWrongScreen)
        {
            if (isClarification)
                return WorkflowScreenState.ClarificationPending;

            if (isWrongScreen || context.IsWrongScreen)
                return WorkflowScreenState.WrongToolOpen;

            if (context.Intent == AdminUserIntent.FolderPermission)
                return DetectFolderPermissionState(context, visualContext);

            if (context.TargetVisible)
                return WorkflowScreenState.TargetVisible;

            if (!string.IsNullOrWhiteSpace(context.Target.Path) || !string.IsNullOrWhiteSpace(context.Target.FolderName))
                return WorkflowScreenState.TargetNotVisible;

            return MapStepToState(context.CurrentStep);
        }

        public WorkflowScreenState DetectNextState(
            WorkflowScreenState current,
            WorkflowStepResult nextStep,
            bool targetVisible,
            bool isClarification)
        {
            if (isClarification)
                return WorkflowScreenState.ClarificationPending;

            if (nextStep.IsComplete)
                return WorkflowScreenState.Completed;

            var stepId = nextStep.StepId.ToLowerInvariant();

            if (stepId.Contains("navigate") || stepId.Contains("sm-redirect"))
                return WorkflowScreenState.WaitingForFolder;

            if (stepId.Contains("security") || stepId.Contains("properties"))
                return WorkflowScreenState.SecurityTabOpen;

            if (stepId.Contains("edit") || stepId.Contains("advanced"))
                return WorkflowScreenState.EditDialogOpen;

            if (stepId.Contains("permission") || stepId.Contains("add"))
                return WorkflowScreenState.PermissionSelected;

            if (stepId.Contains("apply") || stepId.Contains("ok"))
                return WorkflowScreenState.ApplyPending;

            if (targetVisible)
                return WorkflowScreenState.TargetVisible;

            return current == WorkflowScreenState.Unknown
                ? WorkflowScreenState.WaitingForFolder
                : current;
        }

        private static WorkflowScreenState DetectFolderPermissionState(
            WorkflowContext context,
            ScreenVisualContext? visualContext)
        {
            var combined = BuildOcrText(visualContext).ToLowerInvariant();

            if (combined.Contains("advanced security") || combined.Contains("gelişmiş güvenlik")
                || combined.Contains("permissions for") || combined.Contains("izinler"))
                return WorkflowScreenState.EditDialogOpen;

            if (combined.Contains("security") && combined.Contains("tab"))
                return WorkflowScreenState.SecurityTabOpen;

            if (context.TargetVisible)
                return WorkflowScreenState.TargetVisible;

            if (!string.IsNullOrWhiteSpace(context.Target.Path) || !string.IsNullOrWhiteSpace(context.Target.FolderName))
                return WorkflowScreenState.TargetNotVisible;

            return MapStepToState(context.CurrentStep);
        }

        private static WorkflowScreenState MapStepToState(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                return WorkflowScreenState.Unknown;

            var id = stepId.ToLowerInvariant();
            if (id.Contains("navigate") || id.Contains("sm-redirect") || id == "no_path")
                return WorkflowScreenState.WaitingForFolder;
            if (id.Contains("clarify"))
                return WorkflowScreenState.ClarificationPending;
            if (id.Contains("target") && id.Contains("visible"))
                return WorkflowScreenState.TargetVisible;

            return WorkflowScreenState.Unknown;
        }

        private static string BuildOcrText(ScreenVisualContext? ctx)
        {
            if (ctx == null)
                return string.Empty;

            return string.Join(" ",
                ctx.OcrWindowTitle,
                ctx.OcrStatusBar,
                ctx.StatusBarText,
                ctx.ModalBodyText,
                ctx.ModalTitle,
                ctx.SelectedListItem,
                ctx.OcrSelectedRow);
        }
    }
}
