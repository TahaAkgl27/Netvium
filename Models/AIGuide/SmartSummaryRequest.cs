namespace Netvium.Models.AIGuide
{
    public sealed class SmartSummaryRequest
    {
        public required string RequestId { get; init; }
        public required VisionObservation Observation { get; init; }
        public string? ObservationJson { get; init; }
        public RuntimeContext? RuntimeContext { get; init; }
    }
}
