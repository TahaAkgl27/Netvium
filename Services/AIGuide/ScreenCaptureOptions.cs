using System.Windows;

using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class ScreenCaptureOptions
    {
        public required FrameworkElement CaptureRoot { get; init; }

        public ScreenCaptureSource Source { get; init; } = ScreenCaptureSource.Unknown;

        public int MonitorIndex { get; init; }

        public string? ServerKey { get; init; }

        public string? ServerName { get; init; }

        public Window? ClipToWindow { get; init; }

        public nint RdpHostHandle { get; init; }

        public nint AppWindowHandle { get; init; }

        public Func<bool>? IsRdpSessionConnected { get; init; }

        public FrameworkElement? ParentCaptureRoot { get; init; }

        public CapturePhysicalRect? ResolvedBounds { get; init; }

        public CapturePhysicalRect? ParentBounds { get; init; }

        public CapturePhysicalRect? ClipWindowBounds { get; init; }

        public CapturePhysicalRect? VisibleScreenBounds { get; init; }

        public double DpiScale { get; init; } = 1.0;

        public string TargetElementName { get; init; } = string.Empty;

        public string RdpStateLabel { get; init; } = string.Empty;

        public bool UsesPreResolvedBounds => ResolvedBounds != null;
    }
}
