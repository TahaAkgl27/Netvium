namespace Netvium.Services.AIGuide
{
    public sealed class GuidanceProviderOptions
    {
        public required string Provider { get; init; }
        public required string ApiKey { get; init; }
        public string Endpoint { get; init; } = string.Empty;
        public required string Model { get; init; }
        public int TimeoutSeconds { get; init; } = 45;
    }
}
