using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Detects when visible selection does not match user target.</summary>
    public sealed class WrongObjectDetector
    {
        public WrongObjectResult Detect(
            AdminUserIntent intent,
            WorkflowTarget target,
            ScreenVisualContext? visualContext,
            bool targetVisible,
            bool useTurkish)
        {
            if (intent != AdminUserIntent.FolderPermission || visualContext == null)
                return new WrongObjectResult();

            var selected = visualContext.SelectedListItem ?? visualContext.OcrSelectedRow ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selected))
                return new WrongObjectResult();

            var expectedFolder = !string.IsNullOrWhiteSpace(target.FolderName)
                ? target.FolderName
                : ExtractFolderFromPath(target.Path);

            if (string.IsNullOrWhiteSpace(expectedFolder))
                return new WrongObjectResult();

            if (selected.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase))
                return new WrongObjectResult();

            if (!LooksLikeFileNotFolder(selected))
                return new WrongObjectResult();

            var rules = KnowledgePackHost.Current.WorkflowDefinitions.WrongObject;
            return new WrongObjectResult
            {
                Detected = true,
                SelectedItem = selected,
                ExpectedTarget = expectedFolder,
                Message = rules.Message.Format(useTurkish, new Dictionary<string, string>
                {
                    ["selected"] = selected,
                    ["expected"] = expectedFolder
                })
            };
        }

        private static bool LooksLikeFileNotFolder(string name)
        {
            var extensions = KnowledgePackHost.Current.WorkflowDefinitions.WrongObject.FileExtensions;
            return extensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractFolderFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            var trimmed = path.TrimEnd('\\');
            var idx = trimmed.LastIndexOf('\\');
            return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
        }
    }
}
