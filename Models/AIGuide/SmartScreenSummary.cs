namespace Netvium.Models.AIGuide
{
    public sealed class SmartScreenSummary
    {
        public required string RequestId { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string ScreenStatus { get; init; } = string.Empty;
        public string? ServerName { get; init; }
        public string? DetectedRole { get; init; }
        public string? Application { get; init; }
        public List<string> VisibleItems { get; init; } = new();
        public List<SuggestedTopic> SuggestedTopics { get; init; } = new();
        public string Reminder { get; init; } = "AI Guide explains the screen only. It never controls your computer.";
        public ContextConfidence? Confidence { get; init; }
        public List<VisibleEvidenceSummary> Evidence { get; init; } = new();
        public string FormattedSummary { get; set; } = string.Empty;
        public bool IsLowConfidence { get; init; }

        public static SmartScreenSummary Failed(string requestId, string message) =>
            new()
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = message,
                ScreenStatus = message
            };
    }
}
