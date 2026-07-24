using Netvium.Helpers;

namespace Netvium.Rdp
{
    internal static class RdpBlackScreenDiagnostics
    {
        public static void Log(string checkpoint, EmbeddedRdpClient client, int hostWidth, int hostHeight)
        {
            client.TryReadSessionSnapshot(hostWidth, hostHeight, out var snapshot);
            AppLogger.Debug(
                $"checkpoint={checkpoint} server={snapshot.Server} lastEvent={snapshot.LastEvent} "
                + $"ocxConnected={snapshot.OcxConnected} host={snapshot.HostWidth}x{snapshot.HostHeight} "
                + $"control={snapshot.ControlWidth}x{snapshot.ControlHeight} "
                + $"desktop={snapshot.DesktopWidth}x{snapshot.DesktopHeight} "
                + $"disconnectReason={snapshot.DisconnectReason} extReason={snapshot.ExtendedDisconnectReason} "
                + $"fatalError={snapshot.LastFatalErrorCode} logonError={snapshot.LastLogonErrorCode} "
                + $"state={snapshot.State} clientState={snapshot.ClientState}",
                category: "RDP",
                action: "BlackScreenSnapshot");
        }
    }

    internal sealed class RdpSessionSnapshot
    {
        public string? Server { get; init; }
        public string LastEvent { get; init; } = "none";
        public int? OcxConnected { get; init; }
        public int HostWidth { get; init; }
        public int HostHeight { get; init; }
        public int ControlWidth { get; init; }
        public int ControlHeight { get; init; }
        public int DesktopWidth { get; init; }
        public int DesktopHeight { get; init; }
        public int DisconnectReason { get; init; }
        public int ExtendedDisconnectReason { get; init; }
        public int LastFatalErrorCode { get; init; }
        public int LastLogonErrorCode { get; init; }
        public RdpState State { get; init; }
        public int? ClientState { get; init; }
    }
}
