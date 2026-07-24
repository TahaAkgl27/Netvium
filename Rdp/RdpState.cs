namespace Netvium.Rdp
{
    public enum RdpState
    {
        Idle,
        Initializing,
        Connecting,
        Connected,
        LoginComplete,
        Disconnecting,
        Disconnected,
        Reconnecting,
        Failed,
        Disposed
    }
}
