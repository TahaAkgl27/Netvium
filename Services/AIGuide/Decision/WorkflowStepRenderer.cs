using Netvium.Models.AIGuide.KnowledgePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    internal static class WorkflowStepRenderer
    {
        public static string RenderNavigateToPath(string path, bool useTurkish)
        {
            var workflow = KnowledgePackHost.Current.WorkflowDefinitions.Workflows
                .First(w => w.Id == "folder-permission");
            var step = workflow.Steps.First(s => s.Condition == "has_path_and_not_target_visible");
            return step.Instruction.Format(useTurkish, new Dictionary<string, string> { ["path"] = path });
        }
    }
}
