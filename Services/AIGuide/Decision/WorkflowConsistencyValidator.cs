using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Ensures workflow steps belong to the resolved workflow — no cross-workflow leakage.</summary>
    public sealed class WorkflowConsistencyValidator
    {
        private static readonly Dictionary<string, string[]> WorkflowStepPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["folder-permission"] = ["sm-redirect", "navigate", "no_path", "target", "security", "permission", "properties", "clarify", "generic"],
            ["active-directory"] = ["browse", "user", "password", "unlock", "group", "properties", "clarify", "generic"],
            ["dhcp"] = ["scope", "reservation", "lease", "navigate", "clarify", "generic"],
            ["dns"] = ["zone", "record", "host", "ptr", "navigate", "clarify", "generic"],
            ["iis"] = ["site", "binding", "pool", "app", "clarify", "generic"],
            ["event-viewer"] = ["filter", "event", "log", "clarify", "generic"],
            ["clarification"] = ["clarify"],
            ["generic"] = ["generic"]
        };

        public bool IsStepConsistent(string workflowName, string stepId, AdminUserIntent intent)
        {
            if (string.IsNullOrWhiteSpace(workflowName) || string.IsNullOrWhiteSpace(stepId))
                return true;

            if (workflowName == "clarification")
                return stepId.StartsWith("clarify", StringComparison.OrdinalIgnoreCase);

            if (!WorkflowStepPrefixes.TryGetValue(workflowName, out var allowedPrefixes))
                return true;

            var id = stepId.ToLowerInvariant();

            if (allowedPrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal) || id.Contains(p, StringComparison.Ordinal)))
                return true;

            return IsCrossWorkflowViolation(workflowName, id, intent);
        }

        private static bool IsCrossWorkflowViolation(string workflowName, string stepId, AdminUserIntent intent)
        {
            if (workflowName == "folder-permission"
                && (stepId.Contains("dhcp") || stepId.Contains("dns") || stepId.Contains("reservation")))
                return true;

            if (workflowName == "active-directory"
                && (stepId.Contains("dhcp") || stepId.Contains("dns-zone") || stepId.Contains("iis")))
                return true;

            if (workflowName == "dhcp"
                && intent == AdminUserIntent.FolderPermission)
                return true;

            if (workflowName == "dns"
                && (stepId.Contains("folder") || stepId.Contains("ntfs") || stepId.Contains("permission")))
                return true;

            return false;
        }
    }
}
