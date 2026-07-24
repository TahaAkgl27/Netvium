using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Separates confirmed facts from assumptions and unknowns.</summary>
    public sealed class AssumptionDetector
    {
        public AssumptionAnalysis Analyze(
            WorkflowContext context,
            WorkflowGoal goal,
            EvidenceBundle evidence,
            bool clarificationRequired)
        {
            var known = new List<string>();
            var assumed = new List<string>();
            var unknown = new List<string>();

            foreach (var item in evidence.Visual.Where(e => e.IsConfirmed))
                known.Add(item.Statement);

            foreach (var item in evidence.Conversation.Where(e => e.IsConfirmed))
                known.Add(item.Statement);

            foreach (var item in evidence.Knowledge)
                known.Add(item.Statement);

            if (context.ScreenType != MmcConsoleKind.Unknown)
                known.Add($"Screen classified as {context.ScreenDisplayName}");

            if (goal != WorkflowGoal.Unknown && goal != WorkflowGoal.GeneralGuidance)
                known.Add($"Goal identified: {goal}");

            if (context.Intent == AdminUserIntent.FolderPermission)
            {
                if (!string.IsNullOrWhiteSpace(context.Target.Path) && !context.TargetVisible)
                    unknown.Add($"Folder at {context.Target.Path} may not exist or is not visible yet");

                if (string.IsNullOrWhiteSpace(context.Target.UserName) && !clarificationRequired)
                    unknown.Add("Target user is not specified");

                if (context.Target.TargetFromSelection
                    && !context.Target.HasUserProvidedPath
                    && context.Intent == AdminUserIntent.FolderPermission)
                    assumed.Add("Selected list item may be the intended folder");
            }

            if (context.Intent != AdminUserIntent.Unknown && context.WorkflowName == "generic")
                assumed.Add("No specific workflow matched — guidance may be exploratory");

            if (!context.TargetVisible
                && !string.IsNullOrWhiteSpace(context.Target.FolderName)
                && context.Intent == AdminUserIntent.FolderPermission)
                unknown.Add($"Folder '{context.Target.FolderName}' visibility on screen is unconfirmed");

            return new AssumptionAnalysis
            {
                Known = known.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Assumed = assumed,
                Unknown = unknown
            };
        }
    }
}
