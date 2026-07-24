using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Reports verification issues only — does not mutate decisions or responses.</summary>
    public sealed class SelfVerificationEngine
    {
        private readonly ContradictionDetector _contradictionDetector;
        private readonly WorkflowConsistencyValidator _workflowConsistency;

        public SelfVerificationEngine(
            ContradictionDetector contradictionDetector,
            WorkflowConsistencyValidator workflowConsistency)
        {
            _contradictionDetector = contradictionDetector;
            _workflowConsistency = workflowConsistency;
        }

        public SelfVerificationResult Verify(
            WorkflowContext context,
            WorkflowStepResult nextStep,
            EvidenceBundle evidence,
            AssumptionAnalysis assumptions,
            bool isWrongScreen)
        {
            var issues = new List<string>();

            if (isWrongScreen || context.IsWrongScreen)
                issues.Add("Wrong screen for intent");

            if (evidence.TotalCount == 0)
                issues.Add("No evidence collected — response cannot be evidence-backed");

            if (!_workflowConsistency.IsStepConsistent(context.WorkflowName, nextStep.StepId, context.Intent))
                issues.Add($"Step '{nextStep.StepId}' is inconsistent with workflow '{context.WorkflowName}'");

            var contradiction = _contradictionDetector.Detect(evidence, nextStep.Instruction, context, assumptions);
            if (contradiction.HasContradiction)
                issues.AddRange(contradiction.Contradictions);

            if (context.IsClarification && !nextStep.StepId.StartsWith("clarify", StringComparison.OrdinalIgnoreCase))
                issues.Add("Clarification pending but workflow step is not clarify");

            if (assumptions.Unknown.Count > 0
                && !context.IsClarification
                && context.Intent == AdminUserIntent.FolderPermission
                && string.IsNullOrWhiteSpace(context.Target.UserName)
                && string.IsNullOrWhiteSpace(context.Target.Path))
                issues.Add("Missing critical folder path information");

            var passed = issues.Count == 0;

            return new SelfVerificationResult
            {
                Passed = passed,
                WrongScreen = isWrongScreen || context.IsWrongScreen,
                ActionOnHiddenTarget = !context.TargetVisible
                    && context.Intent == AdminUserIntent.FolderPermission
                    && contradiction.Contradictions.Any(c => c.Contains("not visible", StringComparison.OrdinalIgnoreCase)
                        || c.Contains("TargetInvisible", StringComparison.OrdinalIgnoreCase)),
                WorkflowViolation = issues.Any(i => i.Contains("inconsistent", StringComparison.OrdinalIgnoreCase)),
                HasContradiction = contradiction.HasContradiction,
                MissingInformation = assumptions.Unknown.Count > 0,
                CorrectedInstruction = null,
                Issues = issues
            };
        }
    }
}
