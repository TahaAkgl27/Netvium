using Netvium.Models.AIGuide.ExperiencePack;

namespace Netvium.Services.AIGuide.ExperiencePack
{
    public static class ExperiencePackValidator
    {
        public static void Validate(ExperiencePackBundle bundle)
        {
            var errors = new List<string>();

            foreach (var doc in bundle.AllDocuments)
            {
                if (string.IsNullOrWhiteSpace(doc.Domain))
                    errors.Add("ExperiencePack: domain is required.");

                foreach (var entry in doc.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Id))
                        errors.Add($"ExperiencePack ({doc.Domain}): entry id is required.");

                    if (!HasAnyHintContent(entry))
                        errors.Add($"ExperiencePack ({doc.Domain}/{entry.Id}): at least one hint field is required.");
                }
            }

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "AI Guide ExperiencePack missing or invalid." + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        private static bool HasAnyHintContent(ExperienceEntry entry) =>
            entry.CommonMistakes.Count > 0
            || entry.LikelyCauses.Count > 0
            || entry.RiskWarnings.Count > 0
            || entry.VerificationChecks.Count > 0
            || entry.TroubleshootingSteps.Count > 0
            || entry.ExpertTips.Count > 0
            || entry.WhenToAskClarification.Count > 0
            || entry.WhenToEscalate.Count > 0;
    }
}
