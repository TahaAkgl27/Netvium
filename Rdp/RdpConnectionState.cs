namespace Netvium.Rdp
{
    /// <summary>
    /// Manager-level connection lifecycle state. Every attempt must end in a terminal state
    /// (LoginComplete, Failed, TimedOut, Disconnected, or Cancelled) — never infinite Connecting.
    /// </summary>
    public enum RdpConnectionState
    {
        Idle,
        Connecting,
        Connected,
        LoginComplete,
        Failed,
        TimedOut,
        Disconnected,
        Cancelled
    }

    public static class RdpConnectionStateExtensions
    {
        public static bool IsTerminal(this RdpConnectionState state) =>
            state is RdpConnectionState.LoginComplete
                or RdpConnectionState.Failed
                or RdpConnectionState.TimedOut
                or RdpConnectionState.Disconnected
                or RdpConnectionState.Cancelled;

        public static bool IsInProgress(this RdpConnectionState state) =>
            state is RdpConnectionState.Connecting or RdpConnectionState.Connected;

        public static bool IsSessionConnected(this RdpConnectionState state) =>
            state is RdpConnectionState.Connected or RdpConnectionState.LoginComplete;
    }
}
