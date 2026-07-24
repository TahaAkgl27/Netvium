namespace Netvium.Services.AIGuide
{
    public sealed class VisionProviderOptions
    {
        public required string Provider { get; init; }
        public required string ApiKey { get; init; }
        public string? Endpoint { get; init; }
        public required string Model { get; init; }
        public int TimeoutSeconds { get; init; } = 30;
        public string? CaptureWarning { get; init; }
    }
}
