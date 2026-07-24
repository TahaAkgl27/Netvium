using System.Drawing;
using System.Drawing.Imaging;
using Netvium.Models.AIGuide;
using Netvium.Native;

namespace Netvium.Services.AIGuide
{
    internal static class RdpWindowCaptureHelper
    {
        public static byte[]? TryCopyFromScreen(CapturePhysicalRect rect)
        {
            if (rect.Width < 8 || rect.Height < 8)
                return null;

            try
            {
                using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        rect.X,
                        rect.Y,
                        0,
                        0,
                        new Size(rect.Width, rect.Height),
                        CopyPixelOperation.SourceCopy);
                }

                return CaptureImageAnalyzer.EncodePng(bitmap);
            }
            catch
            {
                return null;
            }
        }

        public static byte[]? TryPrintWindow(nint hwnd, CapturePhysicalRect? boundsHint = null)
        {
            if (hwnd == nint.Zero || !NativeMethods.IsWindow(hwnd))
                return null;

            int width;
            int height;
            if (boundsHint != null && boundsHint.Value.Width >= 8 && boundsHint.Value.Height >= 8)
            {
                width = boundsHint.Value.Width;
                height = boundsHint.Value.Height;
            }
            else if (NativeMethods.GetClientRect(hwnd, out var client) && client.Right - client.Left >= 8)
            {
                width = client.Right - client.Left;
                height = client.Bottom - client.Top;
            }
            else if (NativeMethods.GetWindowRect(hwnd, out var window))
            {
                width = window.Right - window.Left;
                height = window.Bottom - window.Top;
            }
            else
            {
                return null;
            }

            if (width < 8 || height < 8)
                return null;

            try
            {
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var hdc = graphics.GetHdc();
                    try
                    {
                        var ok = NativeMethods.PrintWindow(
                            hwnd,
                            hdc,
                            NativeMethods.PW_RENDERFULLCONTENT);
                        if (!ok)
                            ok = NativeMethods.PrintWindow(hwnd, hdc, 0);
                        if (!ok)
                            return null;
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                return CaptureImageAnalyzer.EncodePng(bitmap);
            }
            catch
            {
                return null;
            }
        }
    }
}
