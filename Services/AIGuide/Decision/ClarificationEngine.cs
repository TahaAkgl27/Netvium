using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.KnowledgePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Asks clarifying questions when critical workflow fields are missing (Knowledge Pack driven).</summary>
    public sealed class ClarificationEngine
    {
        public ClarificationResult Evaluate(
            AdminUserIntent intent,
            WorkflowTarget target,
            ScreenVisualContext? visualContext,
            MmcConsoleKind screenType,
            bool useTurkish,
            string userQuestion)
        {
            var rules = KnowledgePackHost.Current.WorkflowDefinitions.Clarification.ByIntent;
            if (!rules.TryGetValue(intent.ToString(), out var intentRules))
                return new ClarificationResult();

            return intent switch
            {
                AdminUserIntent.FolderPermission => EvaluateFolderPermission(target, visualContext, useTurkish, userQuestion, intentRules),
                AdminUserIntent.PasswordReset or AdminUserIntent.UnlockUser =>
                    EvaluateWithSkipKeywords(target, useTurkish, userQuestion, intentRules),
                AdminUserIntent.UserManagement => EvaluateUserManagement(target, useTurkish, userQuestion, intentRules),
                AdminUserIntent.GroupMembership => EvaluateMissingUser(target, useTurkish, intentRules),
                AdminUserIntent.DhcpReservation => EvaluateDhcpReservation(target, useTurkish, intentRules),
                AdminUserIntent.DnsRecordCreation => EvaluateDnsRecord(target, useTurkish, intentRules),
                AdminUserIntent.IisBindingOrAppPool => EvaluateIis(userQuestion, target, useTurkish, intentRules),
                _ => new ClarificationResult()
            };
        }

        private static ClarificationResult EvaluateFolderPermission(
            WorkflowTarget target,
            ScreenVisualContext? visualContext,
            bool tr,
            string question,
            ClarificationIntentRules rules)
        {
            var hasPath = !string.IsNullOrWhiteSpace(target.Path);
            var selectedFolder = GetSelectedFolder(visualContext);
            var hasTargetLocation = hasPath || !string.IsNullOrWhiteSpace(target.FolderName)
                                    || !string.IsNullOrWhiteSpace(selectedFolder);

            var selectedItem = visualContext?.SelectedListItem ?? visualContext?.OcrSelectedRow ?? string.Empty;
            if (selectedItem.Contains('.', StringComparison.Ordinal) && !hasPath)
                return new ClarificationResult();

            if (!hasTargetLocation)
            {
                return BuildResult(rules, tr, "missingFolderPath",
                    [Label(rules, "folderPath", tr)], "Low");
            }

            var targetVisible = WorkflowGuideSupport.IsTargetVisibleOnScreen(target, visualContext);
            if (!targetVisible)
                return new ClarificationResult();

            if (string.IsNullOrWhiteSpace(target.UserName)
                && (FolderPermissionExtractor.IsPermissionQuestion(question)
                    || FolderPermissionExtractor.HasExplicitPermissionLevel(question)))
            {
                return BuildResult(rules, tr, "missingUser",
                    [Label(rules, "userName", tr)], "Medium");
            }

            if (string.IsNullOrWhiteSpace(target.PermissionLevel)
                && FolderPermissionExtractor.HasExplicitPermissionLevel(question))
            {
                return BuildResult(rules, tr, "missingPermission",
                    [Label(rules, "permissionLevel", tr)], "Medium");
            }

            return new ClarificationResult();
        }

        private static ClarificationResult EvaluateWithSkipKeywords(
            WorkflowTarget target,
            bool tr,
            string question,
            ClarificationIntentRules rules)
        {
            if (ContainsAny(Normalize(question), rules.SkipWhenKeywords))
                return new ClarificationResult();

            return EvaluateMissingUser(target, tr, rules);
        }

        private static ClarificationResult EvaluateUserManagement(
            WorkflowTarget target,
            bool tr,
            string question,
            ClarificationIntentRules rules)
        {
            var q = Normalize(question);
            if (MatchesTrigger(q, rules, "ambiguousPermission"))
            {
                return BuildResult(rules, tr, "ambiguousPermission",
                [
                    Label(rules, "userName", tr),
                    Label(rules, "operationType", tr)
                ], "Medium");
            }

            if (string.IsNullOrWhiteSpace(target.UserName)
                && ContainsAny(q, rules.SkipWhenKeywords))
                return new ClarificationResult();

            return EvaluateMissingUser(target, tr, rules);
        }

        private static ClarificationResult EvaluateMissingUser(
            WorkflowTarget target,
            bool tr,
            ClarificationIntentRules rules)
        {
            if (!string.IsNullOrWhiteSpace(target.UserName))
                return new ClarificationResult();

            return BuildResult(rules, tr, "missingUser",
                [Label(rules, "userName", tr)], "Medium");
        }

        private static ClarificationResult EvaluateDhcpReservation(
            WorkflowTarget target,
            bool tr,
            ClarificationIntentRules rules)
        {
            if (string.IsNullOrWhiteSpace(target.IpAddress))
            {
                return BuildResult(rules, tr, "missingIp",
                    [Label(rules, "ipAddress", tr)], "Medium");
            }

            if (string.IsNullOrWhiteSpace(target.MacAddress))
            {
                return BuildResult(rules, tr, "missingMac",
                    [Label(rules, "macAddress", tr)], "Medium");
            }

            return new ClarificationResult();
        }

        private static ClarificationResult EvaluateDnsRecord(
            WorkflowTarget target,
            bool tr,
            ClarificationIntentRules rules)
        {
            if (!string.IsNullOrWhiteSpace(target.IpAddress) || !string.IsNullOrWhiteSpace(target.RecordName))
                return new ClarificationResult();

            return BuildResult(rules, tr, "missingValue",
                [Label(rules, "recordValue", tr)], "Medium");
        }

        private static ClarificationResult EvaluateIis(
            string userQuestion,
            WorkflowTarget target,
            bool tr,
            ClarificationIntentRules rules)
        {
            var q = Normalize(userQuestion);
            if (string.IsNullOrWhiteSpace(target.SiteName)
                && MatchesTrigger(q, rules, "missingSite")
                && !ContainsAny(q, rules.SkipWhenKeywords))
            {
                return BuildResult(rules, tr, "missingSite",
                    [Label(rules, "siteName", tr)], "Medium");
            }

            return new ClarificationResult();
        }

        private static ClarificationResult BuildResult(
            ClarificationIntentRules rules,
            bool tr,
            string questionKey,
            IReadOnlyList<string> missingFields,
            string confidenceLabel)
        {
            if (!rules.Questions.TryGetValue(questionKey, out var question))
                return new ClarificationResult();

            return new ClarificationResult
            {
                Required = true,
                MissingFields = missingFields,
                Question = question.Get(tr),
                ConfidenceLabel = confidenceLabel
            };
        }

        private static string Label(ClarificationIntentRules rules, string key, bool tr) =>
            rules.FieldLabels.TryGetValue(key, out var label) ? label.Get(tr) : key;

        private static string GetSelectedFolder(ScreenVisualContext? ctx)
        {
            var selected = ctx?.SelectedListItem ?? ctx?.OcrSelectedRow ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selected) || selected.Contains('.', StringComparison.Ordinal))
                return string.Empty;
            return selected;
        }

        private static bool MatchesTrigger(string haystack, ClarificationIntentRules rules, string triggerKey)
        {
            if (!rules.TriggerKeywords.TryGetValue(triggerKey, out var keywords))
                return false;
            return ContainsAny(haystack, keywords);
        }

        private static bool ContainsAny(string haystack, IEnumerable<string> needles) =>
            needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

        private static string Normalize(string text) => (text ?? string.Empty).ToLowerInvariant();
    }
}
