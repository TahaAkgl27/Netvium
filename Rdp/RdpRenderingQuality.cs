namespace Netvium.Rdp
{
    public enum RdpConnectionQuality
    {
        Lan,
        Wan,
        Auto
    }

    /// <summary>
    /// Applies MS RDP ActiveX advanced settings for visual quality and bandwidth profiles.
    /// </summary>
    public static class RdpRenderingQuality
    {
        // PerformanceFlags bit masks (mstsc convention)
        private const int PerfDisableWallpaper = 0x00000001;
        private const int PerfDisableFullWindowDrag = 0x00000002;
        private const int PerfDisableMenuAnims = 0x00000004;
        private const int PerfDisableThemes = 0x00000008;
        private const int PerfEnableEnhancedGraphics = 0x00000010;
        private const int PerfDisableCursorShadow = 0x00000020;
        private const int PerfEnableFontSmoothing = 0x00000080;
        private const int PerfEnableDesktopComposition = 0x00000100;

        public static void ApplyProfile(dynamic? adv, RdpConnectionQuality quality)
        {
            if (adv == null) return;

            switch (quality)
            {
                case RdpConnectionQuality.Lan:
                    ApplyLan(adv);
                    break;
                case RdpConnectionQuality.Wan:
                    ApplyWan(adv);
                    break;
                default:
                    ApplyBalanced(adv);
                    break;
            }
        }

        public static void ApplyBaseQuality(dynamic adv)
        {
            // Keep this list to properties actually exposed by AdvancedSettings8.
            // Failed late-bound COM writes are expensive during connection startup.
            try { adv.BitmapPersistence = 1; } catch { /* optional */ }
            try { adv.ShadowBitmap = 1; } catch { /* optional */ }
            try { adv.SmoothScroll = 1; } catch { /* optional */ }
            try { adv.BitmapCacheSize = 32; } catch { /* optional - valid legacy range is 1..32 KB */ }
            try { adv.Compress = 1; } catch { /* optional */ }
            try { adv.allowBackgroundInput = 1; } catch { /* optional */ }
            try { adv.EnableWindowsKey = true; } catch { /* optional */ }
            try { adv.GrabFocusOnConnect = true; } catch { /* optional */ }
            try
            {
                adv.PerformanceFlags =
                    PerfEnableEnhancedGraphics
                    | PerfEnableFontSmoothing
                    | PerfEnableDesktopComposition;
            }
            catch { /* optional */ }
        }

        private static void ApplyLan(dynamic adv)
        {
            ApplyBaseQuality(adv);
            // Compression improves responsiveness even on LAN and does not reduce color depth.
            try { adv.Compress = 1; } catch { /* optional */ }
            try { adv.BandwidthDetection = false; } catch { /* optional */ }
            try { adv.NetworkConnectionType = 6u; } catch { /* optional */ }
            try
            {
                adv.PerformanceFlags =
                    PerfEnableEnhancedGraphics
                    | PerfEnableFontSmoothing
                    | PerfEnableDesktopComposition;
            }
            catch { /* optional */ }
        }

        private static void ApplyWan(dynamic adv)
        {
            ApplyBaseQuality(adv);
            try { adv.Compress = 1; } catch { /* optional */ }
            try { adv.BandwidthDetection = false; } catch { /* optional */ }
            try { adv.NetworkConnectionType = 5u; } catch { /* optional */ }
            try
            {
                adv.PerformanceFlags =
                    PerfEnableEnhancedGraphics
                    | PerfEnableFontSmoothing
                    | PerfEnableDesktopComposition
                    | PerfDisableWallpaper
                    | PerfDisableMenuAnims
                    | PerfDisableFullWindowDrag;
            }
            catch { /* optional */ }
        }

        private static void ApplyBalanced(dynamic adv)
        {
            ApplyBaseQuality(adv);
            try { adv.Compress = 1; } catch { /* optional */ }
            try { adv.BandwidthDetection = true; } catch { /* optional */ }
            try { adv.NetworkConnectionType = 6u; } catch { /* optional */ }
            try
            {
                adv.PerformanceFlags =
                    PerfEnableEnhancedGraphics
                    | PerfEnableFontSmoothing
                    | PerfEnableDesktopComposition;
            }
            catch { /* optional */ }
        }

        public static RdpConnectionQuality ResolveAutoProfile(int? latencyMs)
        {
            if (!latencyMs.HasValue || latencyMs.Value <= 0)
                return RdpConnectionQuality.Lan;
            return latencyMs.Value <= 40 ? RdpConnectionQuality.Lan : RdpConnectionQuality.Wan;
        }
    }
}
