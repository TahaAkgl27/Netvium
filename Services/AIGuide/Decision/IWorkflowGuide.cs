using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    public interface IWorkflowGuide
    {
        string WorkflowName { get; }

        bool CanHandle(AdminUserIntent intent, MmcConsoleKind screenType);

        WorkflowContext BuildContext(WorkflowGuideBuildInput input);

        WorkflowStepResult GetNextStep(WorkflowContext context);
    }

    public sealed class WorkflowGuideBuildInput
    {
        public required string UserQuestion { get; init; }
        public ScreenVisualContext? VisualContext { get; init; }
        public MmcConsoleKind ScreenType { get; init; }
        public string ScreenDisplayName { get; init; } = string.Empty;
        public AdminUserIntent Intent { get; init; }
        public WorkflowTarget Target { get; init; } = new();
        public bool UseTurkish { get; init; }
    }
}
