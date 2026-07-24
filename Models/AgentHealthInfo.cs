using System;

namespace Netvium.Models
{
    public enum AgentHealthStatus
    {
        Offline,
        Warning,
        Online
    }

    public sealed class AgentHealthInfo
    {
        public AgentHealthStatus Status { get; init; } = AgentHealthStatus.Offline;
        public string? Version { get; init; }
        public DateTime? LastHeartbeatUtc { get; init; }
        public long? LatencyMs { get; init; }
        public bool EndpointReachable { get; init; }
        public string? Detail { get; init; }
    }
}
