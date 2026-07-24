namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Identifies what was captured. Multi-monitor uses <see cref="ScreenCaptureOptions.MonitorIndex"/>.
    /// </summary>
    public enum ScreenCaptureSource
    {
        Unknown = 0,
        RdpSession,
        LocalDashboard,
        LocalWindow
    }
}
