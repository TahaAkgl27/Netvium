using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Reliability score — assumptions and evidence quality only. Does not affect Confidence.</summary>
    public sealed class ReliabilityScoreEngine
    {
        public ReliabilityEvaluation Evaluate(
            EvidenceBundle evidence,
            WorkflowGoal goal,
            bool workflowMatched,
            bool targetVisible,
            bool verificationPassed,
            bool hasContradiction,
            AssumptionAnalysis assumptions,
            bool isWrongScreen)
        {
            if (isWrongScreen)
                return Build(0, "Low", EmptyCoverage(), 0, 0, 0, 0, 0, ["WrongScreen"]);

            var coverage = new EvidenceCoverageBreakdown
            {
                Visual = evidence.Visual.Count,
                Conversation = evidence.Conversation.Count,
                Knowledge = evidence.Knowledge.Count,
                Workflow = evidence.Workflow.Count
            };

            var factors = new List<string>();
            var score = 0;

            var minEvidence = 3;
            var coverageScore = Math.Min(25, coverage.Total * 25 / Math.Max(minEvidence, 1));
            score += coverageScore;
            factors.Add($"Coverage={coverageScore}(V{coverage.Visual}/C{coverage.Conversation}/K{coverage.Knowledge}/W{coverage.Workflow})");

            var workflowMatch = workflowMatched ? 20 : 0;
            score += workflowMatch;
            if (workflowMatched) factors.Add("WorkflowMatch");

            var goalMatch = goal != WorkflowGoal.Unknown && goal != WorkflowGoal.GeneralGuidance ? 15 : 5;
            score += goalMatch;
            factors.Add($"GoalMatch={goalMatch}");

            var visualConfirmation = targetVisible ? 20 : 5;
            score += visualConfirmation;
            factors.Add($"VisualConfirmation={visualConfirmation}");

            var contradictionCheck = hasContradiction ? 0 : 15;
            score += contradictionCheck;
            if (!hasContradiction) factors.Add("NoContradiction");

            var assumptionPenalty = Math.Min(25, assumptions.AssumptionCount * 8 + assumptions.Unknown.Count * 5);
            score = Math.Max(0, score - assumptionPenalty);
            if (assumptionPenalty > 0)
                factors.Add($"AssumptionPenalty=-{assumptionPenalty}");

            if (!verificationPassed)
            {
                score = Math.Max(0, score - 20);
                factors.Add("VerificationFailed=-20");
            }

            var label = score switch
            {
                >= 80 => "High",
                >= 55 => "Medium",
                _ => "Low"
            };

            return Build(score, label, coverage, workflowMatch, goalMatch, visualConfirmation,
                contradictionCheck, assumptionPenalty, factors);
        }

        private static EvidenceCoverageBreakdown EmptyCoverage() => new();

        private static ReliabilityEvaluation Build(
            int score,
            string label,
            EvidenceCoverageBreakdown coverage,
            int workflowMatch,
            int goalMatch,
            int visualConfirmation,
            int contradictionCheck,
            int assumptionPenalty,
            IReadOnlyList<string> factors) =>
            new()
            {
                Score = score,
                Label = label,
                Coverage = coverage,
                WorkflowMatch = workflowMatch,
                GoalMatch = goalMatch,
                VisualConfirmation = visualConfirmation,
                ContradictionCheck = contradictionCheck,
                AssumptionPenalty = assumptionPenalty,
                Factors = factors
            };
    }
}
