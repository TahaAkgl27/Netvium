namespace Netvium.Models.AIGuide
{
    public sealed class ServerContext
    {
        public string? ServerName { get; init; }
        public string? IpAddress { get; init; }
        public string? Hostname { get; init; }
        public string? ConnectedUsername { get; init; }
        public string? Domain { get; init; }
        public string? OperatingSystem { get; init; }
        public string? WindowsVersion { get; init; }
        public string ConnectionType { get; init; } = "None";
        public string ConnectionStatus { get; init; } = "Unknown";
        public bool IsAvailable { get; init; }
    }
}
