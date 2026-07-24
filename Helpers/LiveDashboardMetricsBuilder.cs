using System;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class LiveDashboardMetricsBuilder
    {
        public static LiveDashboardMetrics FromServer(ServerInfo server, string serverKey) =>
            new()
            {
                ServerKey = serverKey,
                TimestampUtc = DateTime.UtcNow,
                IpAddress = server.Ip,
                HostName = server.Name,
                IsLive = false,
                Source = LiveDashboardMetricSources.Server,
                NetworkState = NetworkDisplayState.Idle
            };

        public static LiveDashboardMetrics FromSnapshot(
            WmiSnapshot? snap,
            ServerInfo server,
            string serverKey,
            long? latencyMs,
            bool isLive,
            NetworkThroughputTracker? networkTracker = null,
            bool recordNetwork = true)
        {
            var ip = ResolveIpAddress(snap, server);
            var (uptimeText, uptimeDetail) = FormatUptimePair(snap?.Uptime);

            double? disk = snap?.DiskUsedPct;
            if (!disk.HasValue && snap != null && snap.DiskTotalGb > 0)
                disk = Math.Round((snap.DiskTotalGb - snap.DiskFreeGb) / snap.DiskTotalGb * 100, 1);

            double? networkIn = null;
            double? networkOut = null;
            var networkState = NetworkDisplayState.Idle;

            if (isLive && recordNetwork)
            {
                if (snap != null && networkTracker != null
                    && snap.NetworkBytesReceived.HasValue && snap.NetworkBytesSent.HasValue)
                {
                    var throughput = networkTracker.Record(
                        serverKey,
                        snap.NetworkBytesReceived.Value,
                        snap.NetworkBytesSent.Value,
                        DateTime.UtcNow,
                        snap.NetworkCountersAvailable);

                    networkState = throughput.State;
                    networkIn = throughput.DownloadBps;
                    networkOut = throughput.UploadBps;
                }
                else if (snap != null && !snap.NetworkCountersAvailable)
                {
                    networkState = NetworkDisplayState.Unavailable;
                }
                else
                {
                    networkState = NetworkDisplayState.Collecting;
                }
            }

            return new LiveDashboardMetrics
            {
                ServerKey = serverKey,
                TimestampUtc = DateTime.UtcNow,
                CpuPercent = snap?.CpuPct,
                RamPercent = snap?.RamUsedPct,
                DiskPercent = disk,
                NetworkInBps = networkIn,
                NetworkOutBps = networkOut,
                LatencyMs = latencyMs ?? (snap?.PingMs >= 0 ? snap.PingMs : null),
                UptimeText = uptimeText,
                UptimeDetailText = uptimeDetail,
                IpAddress = ip,
                HostName = server.Name,
                IsLive = isLive,
                Source = isLive ? LiveDashboardMetricSources.Live : LiveDashboardMetricSources.Snapshot,
                NetworkState = networkState
            };
        }

        public static string ResolveIpAddress(WmiSnapshot? snap, ServerInfo server)
        {
            if (!string.IsNullOrWhiteSpace(snap?.Ip))
                return snap.Ip;

            var ipv4 = snap?.IPAddresses?
                .FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip) && !ip.Contains(':'));
            if (!string.IsNullOrWhiteSpace(ipv4))
                return ipv4;

            return server.Ip;
        }

        public static (string Text, string Detail) FormatUptimePair(TimeSpan? uptime)
        {
            if (!uptime.HasValue)
                return ("—", "—");

            var u = uptime.Value;
            var text = u.TotalDays >= 1
                ? $"{(int)u.TotalDays}g {u.Hours}s {u.Minutes}d"
                : $"{u.Hours}s {u.Minutes}d";

            var detail = u.TotalDays >= 1
                ? $"{(int)u.TotalDays} gün {u.Hours} saat çalışıyor"
                : $"{u.Hours} saat {u.Minutes} dakika çalışıyor";

            return (text, detail);
        }

        public static string FormatThroughput(double? bps)
        {
            if (!bps.HasValue || bps.Value < 0)
                return "N/A";

            if (bps.Value < 1024)
                return $"{bps.Value:0} B/s";
            if (bps.Value < 1024 * 1024)
                return $"{bps.Value / 1024:0} KB/s";

            return $"{bps.Value / (1024 * 1024):0.0} MB/s";
        }
    }

}
