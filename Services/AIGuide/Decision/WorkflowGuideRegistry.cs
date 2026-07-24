using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    public sealed class WorkflowGuideRegistry
    {
        private readonly IReadOnlyList<IWorkflowGuide> _guides;

        public WorkflowGuideRegistry(IEnumerable<IWorkflowGuide> guides)
        {
            _guides = guides
                .OrderByDescending(g => g.WorkflowName != "generic")
                .ToList();
        }

        public IWorkflowGuide Resolve(AdminUserIntent intent, MmcConsoleKind screenType)
        {
            foreach (var guide in _guides)
            {
                if (guide.CanHandle(intent, screenType))
                    return guide;
            }

            return _guides.First(g => g.WorkflowName == "generic");
        }

        public IReadOnlyList<IWorkflowGuide> All => _guides;
    }
}
