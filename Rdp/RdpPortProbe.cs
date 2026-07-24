using System.Net.Sockets;
using Netvium.Helpers;

namespace Netvium.Rdp
{
    public static class RdpPortProbe
    {
        public const int DefaultTimeoutMs = 3000;

        public static async Task<bool> CheckAsync(
            string host,
            int port,
            int timeoutMs = DefaultTimeoutMs,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0)
                return false;

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(timeoutMs);
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
                RdpConnectionLogger.TcpOk(host, port);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Debug(
                    $"TCP probe failed host={host} port={port}: {ex.Message}",
                    category: "RDP",
                    action: "PortProbe");
                return false;
            }
        }
    }
}
