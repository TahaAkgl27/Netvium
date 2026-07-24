namespace Netvium.Models.AIGuide
{
    public sealed class QuestionSuggestion
    {
        public required string Question { get; init; }
        public int Priority { get; init; }
    }
}
