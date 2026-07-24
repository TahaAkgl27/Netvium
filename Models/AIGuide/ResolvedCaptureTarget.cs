using Netvium.Models.AIGuide;

namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// UI-thread snapshot of capture target geometry and RDP readiness.
    /// Background capture code must use only this DTO — never touch controls directly.
    /// </summary>
    public sealed class ResolvedCaptureTarget
    {
        public ScreenCaptureSource Source { get; init; } = ScreenCaptureSource.Unknown;
        public string TargetName { get; init; } = string.Empty;
        public CapturePhysicalRect? BoundsInScreen { get; init; }
        public CapturePhysicalRect? ParentBoundsInScreen { get; init; }
        public CapturePhysicalRect? ClipWindowBounds { get; init; }
        public double DpiScale { get; init; } = 1.0;
        public bool IsVisible { get; init; }
        public bool IsConnected { get; init; }
        public bool IsReady { get; init; }
        public bool IsConnecting { get; init; }
        public string RdpState { get; init; } = string.Empty;
        public nint RdpHostHandle { get; init; }
        public nint AppWindowHandle { get; init; }
        public CapturePhysicalRect? VisibleScreenBounds { get; init; }
        public string? ServerKey { get; init; }
        public string? ServerName { get; init; }

        /// <summary>WPF element for RenderTargetBitmap capture (dashboard/local). Null for pre-resolved RDP bounds.</summary>
        public System.Windows.FrameworkElement? WpfCaptureRoot { get; init; }

        public bool HasTarget =>
            Source == ScreenCaptureSource.RdpSession
                ? BoundsInScreen != null
                : WpfCaptureRoot != null;
    }
}
