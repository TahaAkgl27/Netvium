using Netvium.Models;

namespace Netvium.Rdp
{
    /// <summary>
    /// Virtual desktop resolution requested at RDP connect — independent of local viewport size.
    /// SmartSizing scales the remote session to fill the viewport (mRemoteNG-style).
    /// </summary>
    public enum RdpResolutionProfile
    {
        Balanced = 0,
        Compact = 1,
        FullHd = 2,
        Native = 3,
    }

    public readonly struct RdpSizingPlan
    {
        public RdpResolutionProfile Profile { get; init; }
        public double ViewportDipWidth { get; init; }
        public double ViewportDipHeight { get; init; }
        public int ViewportPixelWidth { get; init; }
        public int ViewportPixelHeight { get; init; }
        public int RequestedDesktopWidth { get; init; }
        public int RequestedDesktopHeight { get; init; }
        public double SidebarWidthDip { get; init; }

        public string ProfileLabel => RdpViewportSizing.GetProfileLabel(Profile);
    }

    public static class RdpViewportSizing
    {
        public const int CompactWidth = 1366;
        public const int CompactHeight = 768;
        public const int BalancedWidth = 1600;
        public const int BalancedHeight = 900;
        public const int FullHdWidth = 1920;
        public const int FullHdHeight = 1080;

        public static RdpResolutionProfile DefaultProfile => RdpResolutionProfile.Balanced;

        public static string GetProfileLabel(RdpResolutionProfile profile) => profile switch
        {
            RdpResolutionProfile.Compact => "Compact",
            RdpResolutionProfile.Balanced => "Balanced",
            RdpResolutionProfile.FullHd => "FullHD",
            RdpResolutionProfile.Native => "Native",
            _ => profile.ToString(),
        };

        public static (int Width, int Height) GetProfileDesktopSize(
            RdpResolutionProfile profile,
            int viewportPixelWidth,
            int viewportPixelHeight)
        {
            var vw = Math.Max(640, viewportPixelWidth);
            var vh = Math.Max(480, viewportPixelHeight);

            return profile switch
            {
                RdpResolutionProfile.Compact => (
                    Math.Min(CompactWidth, vw),
                    Math.Min(CompactHeight, vh)),
                RdpResolutionProfile.Balanced => (vw, vh),
                RdpResolutionProfile.FullHd => (
                    Math.Min(FullHdWidth, vw),
                    Math.Min(FullHdHeight, vh)),
                RdpResolutionProfile.Native => (vw, vh),
                _ => (vw, vh),
            };
        }

        public static RdpSizingPlan Compute(
            double viewportDipWidth,
            double viewportDipHeight,
            double dpiScaleX,
            double dpiScaleY,
            RdpResolutionProfile profile,
            double sidebarWidthDip = 0)
        {
            var viewportPxW = DipToPixels(viewportDipWidth, dpiScaleX);
            var viewportPxH = DipToPixels(viewportDipHeight, dpiScaleY);
            var (requestedW, requestedH) = GetProfileDesktopSize(profile, viewportPxW, viewportPxH);
            // Connect path uses host viewport pixels (1:1). Plan documents profile preference only.
            _ = requestedW;
            _ = requestedH;

            return new RdpSizingPlan
            {
                Profile = profile,
                ViewportDipWidth = viewportDipWidth,
                ViewportDipHeight = viewportDipHeight,
                ViewportPixelWidth = viewportPxW,
                ViewportPixelHeight = viewportPxH,
                RequestedDesktopWidth = viewportPxW,
                RequestedDesktopHeight = viewportPxH,
                SidebarWidthDip = sidebarWidthDip,
            };
        }

        public static RdpResolutionProfile MigrateFromReadabilityMode(RdpReadabilityMode mode) => mode switch
        {
            RdpReadabilityMode.Native => RdpResolutionProfile.Native,
            RdpReadabilityMode.Large or RdpReadabilityMode.ExtraLarge => RdpResolutionProfile.FullHd,
            RdpReadabilityMode.Comfort or RdpReadabilityMode.Fit => RdpResolutionProfile.Balanced,
            _ => DefaultProfile,
        };

        public static int DipToPixels(double dip, double dpiScale) =>
            (int)Math.Max(1, Math.Ceiling(dip * dpiScale));

        public static string FormatDiagnostics(RdpSizingPlan plan, int panelW, int panelH, int activeXW, int activeXH) =>
            $"Profile={plan.ProfileLabel}{Environment.NewLine}"
            + $"ViewportDIP={plan.ViewportDipWidth:F0}x{plan.ViewportDipHeight:F0}{Environment.NewLine}"
            + $"ViewportPx={plan.ViewportPixelWidth}x{plan.ViewportPixelHeight}{Environment.NewLine}"
            + $"RequestedRemote={plan.RequestedDesktopWidth}x{plan.RequestedDesktopHeight}{Environment.NewLine}"
            + $"Panel={panelW}x{panelH}{Environment.NewLine}"
            + $"ActiveX={activeXW}x{activeXH}{Environment.NewLine}"
            + $"SidebarDIP={plan.SidebarWidthDip:F0}";
    }
}
