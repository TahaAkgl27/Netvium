using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class CaptureBoundsHelper
    {
        public static CapturePhysicalRect? GetPhysicalScreenRect(FrameworkElement element, Window? clipWindow)
        {
            if (element.ActualWidth < 1 || element.ActualHeight < 1)
                element.UpdateLayout();

            var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight));

            var x = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X));
            var y = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y));
            var right = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X));
            var bottom = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y));

            var width = right - x;
            var height = bottom - y;
            if (width < 8 || height < 8)
                return null;

            if (clipWindow != null)
            {
                var windowRect = GetWindowPhysicalRect(clipWindow);
                if (windowRect != null)
                {
                    var wr = windowRect.Value;
                    var clipped = Intersect(
                        x, y, width, height,
                        wr.X, wr.Y, wr.Width, wr.Height);
                    if (clipped == null)
                        return null;
                    (x, y, width, height) = clipped.Value;
                }
            }

            return new CapturePhysicalRect(x, y, width, height);
        }

        public static CapturePhysicalRect? GetWindowPhysicalRect(Window window)
        {
            var topLeft = window.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = window.PointToScreen(new System.Windows.Point(window.ActualWidth, window.ActualHeight));
            var x = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X));
            var y = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y));
            var width = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X)) - x;
            var height = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y)) - y;
            if (width < 8 || height < 8)
                return null;
            return new CapturePhysicalRect(x, y, width, height);
        }

        public static nint? TryGetWinFormsHostHandle(FrameworkElement root)
        {
            if (root is System.Windows.Forms.Integration.WindowsFormsHost host && host.Handle != nint.Zero)
                return host.Handle;

            for (int i = 0, count = VisualTreeHelper.GetChildrenCount(root); i < count; i++)
            {
                if (VisualTreeHelper.GetChild(root, i) is FrameworkElement child)
                {
                    var handle = TryGetWinFormsHostHandle(child);
                    if (handle != null)
                        return handle;
                }
            }

            return null;
        }

        public static double GetDpiScale(FrameworkElement element)
        {
            var dpi = VisualTreeHelper.GetDpi(element);
            return dpi.PixelsPerInchX / 96.0;
        }

        public static nint? TryGetHwndSourceHandle(FrameworkElement element)
        {
            if (PresentationSource.FromVisual(element) is HwndSource source && source.Handle != nint.Zero)
                return source.Handle;
            return null;
        }

        public static CapturePhysicalRect? GetVisibleScreenBoundsForPoint(int centerX, int centerY)
        {
            try
            {
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(centerX, centerY));
                var wa = screen.WorkingArea;
                if (wa.Width < 8 || wa.Height < 8)
                    return null;
                return new CapturePhysicalRect(wa.X, wa.Y, wa.Width, wa.Height);
            }
            catch
            {
                return null;
            }
        }

        public static CapturePhysicalRect? GetVisibleScreenBoundsForRect(CapturePhysicalRect windowRect)
        {
            var cx = windowRect.X + windowRect.Width / 2;
            var cy = windowRect.Y + windowRect.Height / 2;
            return GetVisibleScreenBoundsForPoint(cx, cy);
        }

        public static CapturePhysicalRect? IntersectRects(CapturePhysicalRect a, CapturePhysicalRect b)
        {
            var clipped = Intersect(a.X, a.Y, a.Width, a.Height, b.X, b.Y, b.Width, b.Height);
            if (clipped == null)
                return null;
            var (x, y, w, h) = clipped.Value;
            return new CapturePhysicalRect(x, y, w, h);
        }

        private static (int X, int Y, int Width, int Height)? Intersect(
            int ax, int ay, int aw, int ah,
            int bx, int by, int bw, int bh)
        {
            var left = Math.Max(ax, bx);
            var top = Math.Max(ay, by);
            var right = Math.Min(ax + aw, bx + bw);
            var bottom = Math.Min(ay + ah, by + bh);
            var width = right - left;
            var height = bottom - top;
            if (width < 8 || height < 8)
                return null;
            return (left, top, width, height);
        }
    }
}
