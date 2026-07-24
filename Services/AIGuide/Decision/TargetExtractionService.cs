using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide;

namespace Netvium.Services.AIGuide.Decision
{
    public sealed class TargetExtractionService
    {
        public WorkflowTarget Extract(
            AdminUserIntent intent,
            string userQuestion,
            bool useTurkish,
            ScreenVisualContext? visualContext = null)
        {
            if (intent == AdminUserIntent.FolderPermission)
                return ExtractFolderPermission(userQuestion, useTurkish, visualContext);

            var ip = IpScopeMatcher.ExtractIpFromQuestion(userQuestion);
            var mac = ExtractMacAddress(userQuestion);
            var recordName = ExtractDnsRecordName(userQuestion);
            var siteName = ExtractSiteName(userQuestion);
            var path = FolderPermissionExtractor.ExtractPath(userQuestion);

            return new WorkflowTarget
            {
                IpAddress = ip,
                MacAddress = mac,
                RecordName = recordName,
                SiteName = siteName,
                UserName = FolderPermissionExtractor.ExtractTargetUserPublic(userQuestion),
                Path = path ?? string.Empty,
                HasUserProvidedPath = path != null
            };
        }

        private static WorkflowTarget ExtractFolderPermission(
            string userQuestion,
            bool useTurkish,
            ScreenVisualContext? visualContext)
        {
            var goal = FolderPermissionExtractor.ExtractGoal(userQuestion, useTurkish);
            var path = goal.TargetPath;
            var folderName = goal.TargetFolderName;
            var fromSelection = false;

            if (string.IsNullOrWhiteSpace(path) && visualContext != null)
            {
                var selected = visualContext.SelectedListItem ?? visualContext.OcrSelectedRow ?? string.Empty;
                var address = visualContext.OcrStatusBar ?? visualContext.StatusBarText
                    ?? visualContext.ActiveWindowTitle ?? string.Empty;
                var questionFolder = goal.TargetFolderName;

                if (!string.IsNullOrWhiteSpace(selected) && !selected.Contains('.', StringComparison.Ordinal))
                {
                    var selectionConflictsWithQuestion = !string.IsNullOrWhiteSpace(questionFolder)
                        && !selected.Equals(questionFolder, StringComparison.OrdinalIgnoreCase);
                    var questionFolderIsDemonstrative = FolderPermissionExtractor.IsDemonstrativeFolderReference(questionFolder);

                    if (questionFolderIsDemonstrative)
                    {
                        folderName = selected;
                        fromSelection = true;
                        if (string.IsNullOrWhiteSpace(path) && address.Contains('\\'))
                        {
                            var addrPath = ExtractPathFromText(address);
                            if (!string.IsNullOrWhiteSpace(addrPath))
                            {
                                path = addrPath.EndsWith('\\')
                                    ? addrPath + selected
                                    : addrPath.TrimEnd('\\') + "\\" + selected;
                            }
                        }
                    }
                    else if (!selectionConflictsWithQuestion)
                    {
                        folderName = string.IsNullOrWhiteSpace(folderName) ? selected : folderName;
                        fromSelection = true;

                        if (address.Contains('\\'))
                        {
                            var addrPath = ExtractPathFromText(address);
                            if (!string.IsNullOrWhiteSpace(addrPath))
                            {
                                path = addrPath.EndsWith('\\')
                                    ? addrPath + selected
                                    : addrPath.TrimEnd('\\') + "\\" + selected;
                            }
                        }
                    }
                    else
                    {
                        folderName = questionFolder;
                    }
                }
            }

            var hasPath = !string.IsNullOrWhiteSpace(path);
            return new WorkflowTarget
            {
                Path = path,
                FolderName = folderName,
                UserName = goal.TargetUser,
                PermissionLevel = goal.PermissionLevel,
                Operation = goal.Operation,
                HasUserProvidedPath = hasPath || fromSelection,
                TargetFromSelection = fromSelection && !goal.HasUserProvidedPath
            };
        }

        private static string ExtractMacAddress(string question)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                question,
                @"([0-9A-Fa-f]{2}([:-][0-9A-Fa-f]{2}){5})",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractDnsRecordName(string question)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                question,
                @"(?:kaydı|record)\s+([A-Za-z0-9._-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractSiteName(string question)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                question,
                @"(?:site|siteyi)\s+([A-Za-z0-9._-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractPathFromText(string text)
        {
            var path = FolderPermissionExtractor.ExtractPath(text);
            return path ?? string.Empty;
        }
    }
}
