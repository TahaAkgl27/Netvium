namespace Netvium.Models.AIGuide
{
    public sealed class LearningTopic
    {
        public required string Label { get; init; }
        public required string TeachPrompt { get; init; }
        public string Group { get; init; } = string.Empty;
    }

    public sealed class LearningTopicGroup
    {
        public required string Name { get; init; }
        public List<LearningTopic> Topics { get; init; } = new();
    }

    public sealed class LearningSuggestion
    {
        public List<LearningTopicGroup> Groups { get; init; } = new();
        public List<LearningTopic> AllTopics { get; init; } = new();
    }
}
