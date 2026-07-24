using Netvium.Services.AIGuide.ExperiencePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Composition root — Knowledge Pack driven workflow guides and reasoning pipeline.</summary>
    public static class AIGuideCompositionRoot
    {
        public static IWorkflowDecisionEngine CreateDecisionEngine()
        {
            AIGuideBootstrap.TryEnsureReady(out _);

            var confidenceEngine = new ConfidenceEngine();
            var hallucinationGuard = new HallucinationGuard();
            var contradictionDetector = new ContradictionDetector();
            var workflowConsistency = new WorkflowConsistencyValidator();

            var reasoningEnricher = new ReasoningPipelineEnricher(
                new EvidenceEngine(),
                new GoalDetectionService(),
                new WorkflowStateDetectionService(),
                new AssumptionDetector(),
                new SelfVerificationEngine(contradictionDetector, workflowConsistency),
                new ReliabilityScoreEngine(),
                new ExperiencePackService());

            var registry = new WorkflowGuideRegistry(CreateWorkflowGuidesFromPack());
            return new WorkflowDecisionEngine(
                new ScreenClassificationService(),
                new IntentDetectionService(),
                new TargetExtractionService(),
                registry,
                new ScreenIntentCompatibilityService(),
                new VisibleObjectsExtractor(),
                confidenceEngine,
                hallucinationGuard,
                new ClarificationEngine(),
                new WrongObjectDetector(),
                workflowConsistency,
                reasoningEnricher,
                new ConversationContinuityService(),
                new WorkflowStepProgressionService());
        }

        public static IReadOnlyList<IWorkflowGuide> CreateWorkflowGuidesFromPack()
        {
            AIGuideBootstrap.TryEnsureReady(out _);
            return KnowledgePackHost.Current.WorkflowDefinitions.Workflows
                .Select(w => (IWorkflowGuide)new KnowledgePackWorkflowGuide(w))
                .ToList();
        }
    }
}
