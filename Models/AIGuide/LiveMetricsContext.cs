namespace Netvium.Models.AIGuide
{
    public sealed class LiveMetricsContext
    {
        public bool IsAvailable { get; init; }
        public string OnlineStatus { get; init; } = "Unknown";
        public double? CpuPercent { get; init; }
        public double? RamPercent { get; init; }
        public double? DiskPercent { get; init; }
        public string? Uptime { get; init; }
        public DateTime? LastRefreshUtc { get; init; }
        public string DataSource { get; init; } = "None";
    }
}
