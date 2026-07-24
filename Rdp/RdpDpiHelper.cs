using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Netvium.Rdp
{
    public static class RdpDpiHelper
    {
        public static int ScalePercentFromDpiScale(double dpiScale) =>
            (int)Math.Clamp(Math.Round(dpiScale * 100.0), 100, 500);

        /// <summary>
        /// RDP monitor attributes accept only 100, 140 or 180 for DeviceScaleFactor.
        /// DesktopScaleFactor may still carry the exact Windows scale (for example 150%).
        /// </summary>
        public static int NormalizeDeviceScalePercent(int scalePercent)
        {
            var scale = Math.Clamp(scalePercent, 100, 500);
            if (scale < 120)
                return 100;
            if (scale < 160)
                return 140;
            return 180;
        }

        public static uint PixelsToPhysicalMillimeters(int pixels, int dpi)
        {
            if (pixels <= 0)
                return 10;

            var safeDpi = dpi > 0 ? dpi : 96;
            return (uint)Math.Clamp(
                (int)Math.Round(pixels * 25.4 / safeDpi),
                10,
                10_000);
        }

        public static (int DesktopScalePercent, int DeviceScalePercent) GetScalePercentsFromVisual(Visual? visual)
        {
            if (visual == null)
                return (100, 100);

            var dpi = VisualTreeHelper.GetDpi(visual);
            var scale = ScalePercentFromDpiScale(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
            return (scale, NormalizeDeviceScalePercent(scale));
        }

        public static RdpSystemDpiSnapshot CaptureSystemDpi(Visual? visual = null)
        {
            var snapshot = new RdpSystemDpiSnapshot
            {
                ProcessDpiAwareness = GetProcessDpiAwareness(),
                ApplicationHighDpiMode = "PerMonitorV2",
            };

            if (visual != null)
            {
                var dpi = VisualTreeHelper.GetDpi(visual);
                snapshot.WpfDpiScaleX = dpi.DpiScaleX;
                snapshot.WpfDpiScaleY = dpi.DpiScaleY;
                snapshot.WpfDpiScalePercent = ScalePercentFromDpiScale(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
            }

            try
            {
                var window = visual is DependencyObject d ? Window.GetWindow(d) : null;
                if (window != null)
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                    if (monitor != nint.Zero)
                    {
                        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                        if (GetMonitorInfo(monitor, ref info))
                        {
                            snapshot.MonitorRect = $"{info.rcMonitor.left},{info.rcMonitor.top},{info.rcMonitor.right},{info.rcMonitor.bottom}";
                        }

                        var dx = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out var dpiY);
                        if (dx == 0)
                        {
                            snapshot.EffectiveDpiX = dpiX;
                            snapshot.EffectiveDpiY = dpiY;
                            snapshot.WindowsScalingPercent = ScalePercentFromDpiScale(dpiX / 96.0);
                        }
                    }
                }
            }
            catch
            {
                /* optional */
            }

            try
            {
                snapshot.PrimaryScreenPixels = $"{System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 0}x{System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 0}";
            }
            catch
            {
                snapshot.PrimaryScreenPixels = "n/a";
            }

            return snapshot;
        }

        private static string GetProcessDpiAwareness()
        {
            try
            {
                if (GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext(), out var awareness) == 0)
                {
                    return awareness switch
                    {
                        0 => "Unaware",
                        1 => "System",
                        2 => "PerMonitor",
                        _ => $"Context({awareness})",
                    };
                }
            }
            catch
            {
                /* optional */
            }

            return "PerMonitorV2 (WPF ApplicationHighDpiMode)";
        }

        private const int MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;

        [DllImport("user32.dll")]
        private static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern nint GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(nint dpiContext, out int awareness);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);
    }

    public sealed class RdpSystemDpiSnapshot
    {
        public string ProcessDpiAwareness { get; set; } = string.Empty;
        public string ApplicationHighDpiMode { get; set; } = string.Empty;
        public double WpfDpiScaleX { get; set; }
        public double WpfDpiScaleY { get; set; }
        public int WpfDpiScalePercent { get; set; }
        public uint EffectiveDpiX { get; set; }
        public uint EffectiveDpiY { get; set; }
        public int WindowsScalingPercent { get; set; }
        public string MonitorRect { get; set; } = string.Empty;
        public string PrimaryScreenPixels { get; set; } = string.Empty;
    }
}
