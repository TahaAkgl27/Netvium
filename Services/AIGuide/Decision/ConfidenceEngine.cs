using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.KnowledgePack;



namespace Netvium.Services.AIGuide.Decision

{

    public sealed class ConfidenceEngine

    {

        public ConfidenceEvaluation Evaluate(ConfidenceInput input)

        {

            var rules = KnowledgePackHost.Current.ConfidenceRules;

            var scoring = rules.Scoring;

            var overrides = rules.Overrides;

            var highThreshold = rules.Thresholds.GetValueOrDefault("high", 90);

            var mediumThreshold = rules.Thresholds.GetValueOrDefault("medium", 70);



            if (input.IsWrongScreen)

                return Final(0, overrides.GetValueOrDefault("wrongScreen", "Low"), ["WrongScreen override"]);



            var factors = new List<string>();

            var score = 0;



            if (input.ScreenCorrect) { score += scoring.GetValueOrDefault("screenCorrect", 30); factors.Add("ScreenCorrect"); }

            if (input.IntentDetected) { score += scoring.GetValueOrDefault("intentDetected", 20); factors.Add("IntentDetected"); }

            if (input.WorkflowMatched) { score += scoring.GetValueOrDefault("workflowMatched", 20); factors.Add("WorkflowMatched"); }

            if (input.TargetVisible) { score += scoring.GetValueOrDefault("targetVisible", 20); factors.Add("TargetVisible"); }

            if (input.TargetVerified) { score += scoring.GetValueOrDefault("targetVerified", 10); factors.Add("TargetVerified"); }



            var label = ScoreToLabel(score, highThreshold, mediumThreshold);



            if (input.IsUnknownScreen && label == "High")

            {

                label = overrides.GetValueOrDefault("unknownScreenMax", "Medium");

                factors.Add("UnknownScreen cap");

            }



            if (!input.TargetVisible && label == "High")

            {

                label = overrides.GetValueOrDefault("targetNotVisibleMax", "Medium");

                factors.Add("TargetNotVisible cap");

            }



            if (input.MissingRequiredTarget && label == "High")

            {

                label = overrides.GetValueOrDefault("missingRequiredTargetMax", "Medium");

                factors.Add("MissingRequiredTarget cap");

            }



            if (input.IsGenericWorkflow && label == "High")

            {

                label = overrides.GetValueOrDefault("genericWorkflowMax", "Medium");

                factors.Add("GenericWorkflow cap");

            }



            if (input.IsServerManagerFolderPermission && label == "High")

            {

                label = overrides.GetValueOrDefault("serverManagerFolderPermissionMax", "Medium");

                factors.Add("ServerManagerFolderPermission cap");

            }



            if (input.TargetSelectedButNotVerified && label == "High")

            {

                label = overrides.GetValueOrDefault("targetSelectedButNotVerifiedMax", "Medium");

                factors.Add("TargetSelectedButNotVerified cap");

            }



            if (input.TargetVisibleAndSelectedAndIntentClear && label == "Medium" && score >= highThreshold)

            {

                label = overrides.GetValueOrDefault("targetVisibleAndSelectedAndIntentClearBoost", "High");

                factors.Add("TargetVisibleAndSelectedAndIntentClear boost");

            }



            if (!input.IsFolderPermissionIntent

                && input.ScreenCorrect

                && input.IntentDetected

                && input.WorkflowMatched

                && label == "Medium"

                && score >= mediumThreshold)

            {

                label = overrides.GetValueOrDefault("nonFolderIntentOnCorrectScreenBoost", "High");

                factors.Add("NonFolderIntentOnCorrectScreen boost");

            }



            if (!input.IntentDetected && input.ScreenCorrect && input.IsExploratoryQuestion && label == "Low" && !input.ClarificationRequired)

            {

                label = overrides.GetValueOrDefault("exploratoryQuestionFloor", "Medium");

                factors.Add("ExploratoryQuestion floor");

            }



            if (input.IntentUnknown && !input.ClarificationRequired)

            {

                if (input.ScreenCorrect && !input.IsUnknownScreen && input.IsExploratoryQuestion)

                {

                    label = overrides.GetValueOrDefault("exploratoryQuestionFloor", "Medium");

                    factors.Add("RecognizedScreenExploration floor");

                }

                else

                {

                    label = overrides.GetValueOrDefault("intentUnknown", "Low");

                    factors.Add("IntentUnknown override");

                }

            }



            if (input.ClarificationRequired)

            {

                label = input.MissingRequiredTarget

                    ? overrides.GetValueOrDefault("clarificationRequiredLow", "Low")

                    : overrides.GetValueOrDefault("clarificationRequiredMedium", "Medium");

                factors.Add("ClarificationRequired override");

            }



            if (input.WrongObjectDetected)

            {

                label = overrides.GetValueOrDefault("wrongObjectDetected", "Low");

                factors.Add("WrongObjectDetected override");

            }



            if (input.HallucinationGuardTriggered)

            {

                label = overrides.GetValueOrDefault("hallucinationTriggered", "Low");

                factors.Add("HallucinationGuardTriggered override");

            }



            return Final(score, label, factors);

        }



        private static string ScoreToLabel(int score, int high, int medium) => score switch

        {

            var s when s >= high => "High",

            var s when s >= medium => "Medium",

            _ => "Low"

        };



        private static ConfidenceEvaluation Final(int score, string label, IReadOnlyList<string> factors) =>

            new() { Score = score, Label = label, Factors = factors };

    }

}


