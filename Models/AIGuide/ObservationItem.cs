namespace Netvium.Models.AIGuide
{
    public sealed class ObservationItem
    {
        public required string Text { get; init; }
        public ObservationSeverity Severity { get; init; } = ObservationSeverity.Info;
        public ObservationCategory Category { get; init; } = ObservationCategory.Vision;
        public int Importance { get; init; }
    }
}
