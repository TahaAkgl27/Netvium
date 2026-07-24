using System;

namespace Netvium.Models
{
    public enum NetworkDisplayState
    {
        Idle,
        Collecting,
        Ready,
        Unavailable
    }

    public static class LiveDashboardMetricSources
    {
        public const string Live = "live";
        public const string Snapshot = "snapshot";
        public const string Server = "server";
    }

    /// <summary>
    /// Single source of truth for dashboard CPU/RAM/Disk/Network/Uptime/IP display.
    /// </summary>
    public sealed class LiveDashboardMetrics
    {
        public string ServerKey { get; init; } = string.Empty;
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public int? CpuPercent { get; init; }
        public double? RamPercent { get; init; }
        public double? DiskPercent { get; init; }
        public double? NetworkInBps { get; init; }
        public double? NetworkOutBps { get; init; }
        public long? LatencyMs { get; init; }
        public string? UptimeText { get; init; }
        public string? UptimeDetailText { get; init; }
        public string IpAddress { get; init; } = string.Empty;
        public string? HostName { get; init; }
        public bool IsLive { get; init; }
        public string Source { get; init; } = LiveDashboardMetricSources.Server;
        public NetworkDisplayState NetworkState { get; init; } = NetworkDisplayState.Idle;

        public bool HasCpu => CpuPercent.HasValue;
        public bool HasRam => RamPercent.HasValue;
        public bool HasDisk => DiskPercent.HasValue;
        public bool HasNetwork => NetworkInBps.HasValue || NetworkOutBps.HasValue;
        public bool HasUptime => !string.IsNullOrWhiteSpace(UptimeText) && UptimeText != "—";
        public bool HasIp => !string.IsNullOrWhiteSpace(IpAddress) && IpAddress != "—";
    }

}
