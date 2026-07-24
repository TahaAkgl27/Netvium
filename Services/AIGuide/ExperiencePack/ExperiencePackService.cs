using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.ExperiencePack;
using Netvium.Models.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.ExperiencePack
{
    /// <summary>Selects read-only field experience hints. Does not affect workflow, confidence, or reliability.</summary>
    public sealed class ExperiencePackService
    {
        private static readonly Regex EventIdPattern = new(
            @"\b(\d{4,5})\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public IReadOnlyList<ExperienceHint> SelectHints(ExperienceSelectionInput input)
        {
            if (input.IsWrongScreen || input.Intent == AdminUserIntent.Unknown)
                return Array.Empty<ExperienceHint>();

            if (!ExperiencePackHost.IsLoaded)
                return Array.Empty<ExperienceHint>();

            var eventId = ResolveEventId(input);
            var question = input.UserQuestion ?? string.Empty;
            var scored = new List<(int Score, ExperienceEntry Entry, ExperiencePackDocument Doc)>();

            foreach (var doc in ExperiencePackHost.Current.AllDocuments)
            {
                foreach (var entry in doc.Entries)
                {
                    var score = ScoreEntry(entry, input, eventId, question);
                    if (score > 0)
                        scored.Add((score + entry.Priority, entry, doc));
                }
            }

            if (scored.Count == 0)
                return Array.Empty<ExperienceHint>();

            var best = scored
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Id, StringComparer.OrdinalIgnoreCase)
                .First();

            return BuildHints(best.Entry, input.UseTurkish);
        }

        private static IReadOnlyList<ExperienceHint> BuildHints(ExperienceEntry entry, bool useTurkish)
        {
            var candidates = new List<(int Order, ExperienceHint Hint)>();
            TryAddHint(candidates, 1, entry, useTurkish, "likelyCause", entry.LikelyCauses);
            TryAddHint(candidates, 2, entry, useTurkish, "expertTip", entry.ExpertTips);
            TryAddHint(candidates, 3, entry, useTurkish, "commonMistake", entry.CommonMistakes);
            TryAddHint(candidates, 4, entry, useTurkish, "verificationCheck", entry.VerificationChecks);
            TryAddHint(candidates, 5, entry, useTurkish, "riskWarning", entry.RiskWarnings);
            TryAddHint(candidates, 6, entry, useTurkish, "troubleshootingStep", entry.TroubleshootingSteps);

            return candidates
                .OrderBy(c => c.Order)
                .Take(2)
                .Select(c => c.Hint)
                .ToList();
        }

        private static void TryAddHint(
            List<(int Order, ExperienceHint Hint)> candidates,
            int order,
            ExperienceEntry entry,
            bool useTurkish,
            string category,
            IReadOnlyList<LocalizedText> texts)
        {
            if (texts.Count == 0)
                return;

            var text = texts[0].Get(useTurkish);
            if (string.IsNullOrWhiteSpace(text))
                return;

            candidates.Add((order, new ExperienceHint
            {
                Id = $"{entry.Id}:{category}",
                Category = category,
                Text = text.Trim(),
                Priority = entry.Priority
            }));
        }

        private static int ScoreEntry(
            ExperienceEntry entry,
            ExperienceSelectionInput input,
            string eventId,
            string question)
        {
            var score = 0;
            var qLower = question.ToLowerInvariant();

            if (entry.Match.EventIds.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(eventId) || !entry.Match.EventIds.Any(e => e == eventId))
                    return 0;
                score += 100;
            }

            if (entry.Match.Intents.Count > 0
                && !entry.Match.Intents.Any(i => i.Equals(input.Intent.ToString(), StringComparison.OrdinalIgnoreCase)))
                return 0;
            if (entry.Match.Intents.Count > 0)
                score += 30;

            if (entry.Match.Goals.Count > 0
                && !entry.Match.Goals.Any(g => g.Equals(input.Goal.ToString(), StringComparison.OrdinalIgnoreCase)))
                return 0;
            if (entry.Match.Goals.Count > 0)
                score += 20;

            if (entry.Match.Workflows.Count > 0
                && !entry.Match.Workflows.Any(w => MatchesToken(w, input.WorkflowName)))
                return 0;
            if (entry.Match.Workflows.Count > 0)
                score += 15;

            if (entry.Match.Screens.Count > 0
                && !entry.Match.Screens.Any(s => s.Equals(input.ScreenType.ToString(), StringComparison.OrdinalIgnoreCase)))
                return 0;
            if (entry.Match.Screens.Count > 0)
                score += 15;

            if (entry.Match.States.Count > 0
                && !entry.Match.States.Any(s => s.Equals(input.CurrentState.ToString(), StringComparison.OrdinalIgnoreCase)))
                return 0;
            if (entry.Match.States.Count > 0)
                score += 10;

            if (entry.Match.QuestionKeywords.Count > 0)
            {
                if (!entry.Match.QuestionKeywords.Any(k => qLower.Contains(k, StringComparison.Ordinal)))
                    return 0;
                score += 15;
            }

            if (entry.Match.Intents.Count == 0
                && entry.Match.Goals.Count == 0
                && entry.Match.Workflows.Count == 0
                && entry.Match.Screens.Count == 0
                && entry.Match.States.Count == 0
                && entry.Match.EventIds.Count == 0
                && entry.Match.QuestionKeywords.Count == 0)
                score += 1;

            return score;
        }

        private static bool MatchesToken(string pattern, string value)
        {
            if (pattern == "*")
                return true;
            return pattern.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveEventId(ExperienceSelectionInput input)
        {
            if (!string.IsNullOrWhiteSpace(input.DetectedEventId))
                return input.DetectedEventId.Trim();

            var match = EventIdPattern.Match(input.UserQuestion ?? string.Empty);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}
