namespace Netvium.Models
{
    /// <summary>
    /// Trend summaries derived from snapshot history (not used for live chart rendering).
    /// </summary>
    public sealed class SnapshotTrendData
    {
        public string? CpuTrend { get; set; }
        public string? RamTrend { get; set; }
        public string? DiskTrend { get; set; }
        public string? EventTrend { get; set; }
        public string? RiskTrend { get; set; }
        public string? HealthTrend { get; set; }
        public int SampleCount { get; set; }
    }
}
