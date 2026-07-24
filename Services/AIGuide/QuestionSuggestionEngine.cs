using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class QuestionSuggestionEngine
    {
        public List<QuestionSuggestion> Suggest(VisualMentorInput input, ObservationResult observations)
        {
            var questions = new List<QuestionSuggestion>();
            var obs = input.Observation;
            var haystack = BuildHaystack(obs);
            var workflow = input.WorkflowState?.Workflow?.Name;
            var localizedWorkflow = AIGuideLanguage.LocalizeWorkflowName(
                input.WorkflowState?.Workflow?.Id,
                workflow ?? string.Empty);

            if (Contains(haystack, "password", "reset"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionResetPassword(), 95);

            if (Contains(haystack, "disabled"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionAccountDisabled(), 90);

            if (Contains(haystack, "locked"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionAccountLocked(), 88);

            if (obs.VisibleErrors.Count > 0 || Contains(haystack, "event id"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionExplainEventId(), 92);

            if (Contains(haystack, "iis") && Contains(haystack, "stopped"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionIisStopped(), 86);

            if (Contains(haystack, "dns", "record", "zone"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionDnsRecord(), 84);

            if (Contains(haystack, "registry", "regedit"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionRegistryValue(), 82);

            if (Contains(haystack, "hyper-v", "virtual machine"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionVmState(), 80);

            if (Contains(haystack, "service") && Contains(haystack, "stopped"))
                Add(questions, AIGuideLocalizedCatalogs.QuestionServiceStopped(), 78);

            if (!string.IsNullOrWhiteSpace(localizedWorkflow))
                Add(questions, AIGuideLocalizedCatalogs.QuestionWorkflowStep(localizedWorkflow), 76);

            if (input.Summary?.SuggestedTopics.Count > 0)
            {
                foreach (var topic in input.Summary.SuggestedTopics.Take(4))
                    Add(questions, topic.Question, 70);
            }

            if (questions.Count == 0)
                Add(questions, AIGuideLocalizedCatalogs.QuestionWhatCanYouSee(), 50);

            Add(questions, AIGuideLocalizedCatalogs.QuestionManualSteps(), 45);

            return questions
                .GroupBy(q => q.Question, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(q => q.Priority).First())
                .OrderByDescending(q => q.Priority)
                .Take(8)
                .ToList();
        }

        private static void Add(List<QuestionSuggestion> list, string question, int priority) =>
            list.Add(new QuestionSuggestion { Question = question, Priority = priority });

        private static bool Contains(string haystack, params string[] terms) =>
            terms.Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));

        private static string BuildHaystack(VisionObservation obs) =>
            string.Join(" ",
                new[] { obs.Application, obs.WindowTitle, obs.CurrentTaskHint }
                .Concat(obs.DetectedSoftware)
                .Concat(obs.VisibleControls.Select(c => c.Label))
                .Concat(obs.VisibleText.Select(t => t.Text))
                .Concat(obs.VisibleErrors.Select(e => e.Message))
                .Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();
    }
}
