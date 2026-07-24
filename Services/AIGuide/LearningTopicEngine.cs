using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class LearningTopicEngine
    {
        public LearningSuggestion Suggest(VisualMentorInput input)
        {
            var application = input.Observation.Application;
            var window = input.Observation.WindowTitle;
            var role = input.RuntimeContext?.DetectedRole.DisplayName;
            return LearningTopicCatalog.Resolve(application, window, role);
        }
    }
}
