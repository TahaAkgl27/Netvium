using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    internal static class WrongScreenGuardBuilder
    {
        public static WorkflowDecisionResult Build(
            AdminUserIntent intent,
            MmcConsoleKind screenType,
            string displayName,
            WorkflowTarget target,
            bool useTurkish,
            ScreenIntentCompatibilityResult compatibility,
            IReadOnlyList<string> visibleObjects,
            ConfidenceEngine confidenceEngine,
            bool isIisFolderPermission = false)
        {
            var screenLabel = ScreenIntentCompatibilityService.ResolveDisplayLabel(screenType, displayName);
            var steps = BuildRedirectSteps(intent, target, useTurkish, screenLabel);
            var observation = BuildObservation(intent, screenLabel, useTurkish, compatibility.IsConsumerApp, isIisFolderPermission);

            var confidence = confidenceEngine.Evaluate(new ConfidenceInput
            {
                IsWrongScreen = true,
                IsUnknownScreen = screenType == MmcConsoleKind.Unknown,
                IntentDetected = intent != AdminUserIntent.Unknown
            });

            var context = new WorkflowContext
            {
                ScreenType = screenType,
                ScreenDisplayName = screenLabel,
                Intent = intent,
                Target = target,
                WorkflowName = "wrong-screen-guard",
                TargetVisible = false,
                UseTurkish = useTurkish,
                ScreenObservation = observation,
                VisibleObjects = visibleObjects,
                AllowedScreens = compatibility.AllowedScreenLabels,
                IsWrongScreen = true,
                IsConsumerApp = compatibility.IsConsumerApp,
                ShouldRefuse = true,
                ShouldContinue = false,
                ShouldAskQuestion = false,
                ConfidenceCandidate = confidence.Label,
                ConfidenceScore = confidence.Score,
                Evidence = visibleObjects.Take(6).ToList(),
                TotalSteps = steps.Count,
                CurrentStep = "redirect"
            };

            return new WorkflowDecisionResult
            {
                HasWorkflow = true,
                UseLocalResponse = true,
                ShouldRefuse = true,
                ShouldContinue = false,
                ShouldAskQuestion = false,
                Context = context,
                NextStep = WorkflowGuideSupport.Step("redirect", 1, steps[0]),
                ManualStepInstructions = steps,
                StructuredSummary = $"WrongScreenGuard; Intent={intent}; Screen={screenLabel}"
            };
        }

        private static string BuildObservation(
            AdminUserIntent intent,
            string screenLabel,
            bool tr,
            bool isConsumer,
            bool isIisFolderPermission)
        {
            var rules = KnowledgePackHost.Current.ScreenCompatibility.WrongScreen.Observations;

            if (intent == AdminUserIntent.FolderPermission && isIisFolderPermission
                && rules.TryGetValue("iisFolderPermission", out var iisObs))
            {
                return iisObs.Get(tr);
            }

            if (intent == AdminUserIntent.FolderPermission && isConsumer
                && rules.TryGetValue("consumerApp", out var consumerObs))
            {
                return consumerObs.Format(tr, new Dictionary<string, string> { ["screenLabel"] = screenLabel });
            }

            return rules.TryGetValue("generic", out var generic)
                ? generic.Get(tr)
                : string.Empty;
        }

        private static List<string> BuildRedirectSteps(
            AdminUserIntent intent,
            WorkflowTarget target,
            bool tr,
            string currentScreen)
        {
            var redirect = KnowledgePackHost.Current.ScreenCompatibility.WrongScreen.RedirectSteps;
            var intentKey = intent.ToString();
            if (!redirect.TryGetValue(intentKey, out var rules))
                rules = redirect.GetValueOrDefault("default") ?? [];

            var steps = new List<string>();
            foreach (var rule in rules)
            {
                if (rule.FallbackWhenNoPath)
                {
                    if (!string.IsNullOrWhiteSpace(target.Path))
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.RequiresToken))
                {
                    var tokenValue = rule.RequiresToken switch
                    {
                        "path" => target.Path,
                        _ => string.Empty
                    };
                    if (string.IsNullOrWhiteSpace(tokenValue))
                        continue;

                    steps.Add(rule.AsLocalized().Format(tr, new Dictionary<string, string>
                    {
                        [rule.RequiresToken] = tokenValue
                    }));
                    continue;
                }

                steps.Add(rule.AsLocalized().Format(tr, new Dictionary<string, string>
                {
                    ["screenLabel"] = currentScreen
                }));
            }

            if (steps.Count == 0)
            {
                steps.Add(tr
                    ? $"Şu an doğru ekranda değilsiniz ({currentScreen})."
                    : $"You are not on the correct screen ({currentScreen}).");
            }

            return steps;
        }
    }
}
