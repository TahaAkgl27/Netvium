using System;

namespace Netvium.Models
{
    public sealed class MetricSample
    {
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public int? CpuPercent { get; init; }
        public double? RamPercent { get; init; }
        public double? DiskPercent { get; init; }
        public double? NetworkInBps { get; init; }
        public double? NetworkOutBps { get; init; }

        public bool HasAnyMetric =>
            CpuPercent.HasValue || RamPercent.HasValue || DiskPercent.HasValue
            || NetworkInBps.HasValue || NetworkOutBps.HasValue;
    }
}
