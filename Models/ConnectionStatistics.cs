namespace Netvium.Models
{
    public sealed class ConnectionStatistics
    {
        public string? MostConnectedServer { get; init; }
        public int TotalConnections { get; init; }
        public int SuccessfulConnections { get; init; }
        public int FailedConnections { get; init; }
        public double AverageConnectionDurationMs { get; init; }
        public long LongestSessionDurationMs { get; init; }
        public int ConnectionsLast30Days { get; init; }
    }
}
