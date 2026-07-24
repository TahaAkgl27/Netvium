namespace Netvium.Models.AIGuide.Decision
{
    public sealed class WrongObjectResult
    {
        public bool Detected { get; init; }
        public string SelectedItem { get; init; } = string.Empty;
        public string ExpectedTarget { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
