using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ScreenFingerprintBuilder
    {
        private const int HashWidth = 9;
        private const int HashHeight = 8;

        public static ScreenFingerprint Build(byte[] pngBytes, ScreenVisualContext? visualContext)
        {
            var hash = ComputeDifferenceHash(pngBytes);
            var app = visualContext != null
                ? MmcConsoleDetector.SnapInDisplayName(visualContext.MmcConsole)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(app) && visualContext != null)
                app = visualContext.MmcSnapIn;

            return new ScreenFingerprint
            {
                PerceptualHash = hash,
                ActiveWindowTitle = Normalize(visualContext?.ActiveWindowTitle ?? visualContext?.OcrWindowTitle),
                DetectedApplication = Normalize(app),
                ApplicationFingerprint = visualContext?.ApplicationFingerprint ?? string.Empty,
                OcrKeywords = BuildOcrKeywords(visualContext)
            };
        }

        public static int HammingDistance(string hashA, string hashB)
        {
            if (string.IsNullOrEmpty(hashA) || string.IsNullOrEmpty(hashB))
                return 64;

            var len = Math.Min(hashA.Length, hashB.Length);
            var distance = Math.Abs(hashA.Length - hashB.Length) * 2;
            for (var i = 0; i < len; i++)
            {
                if (hashA[i] != hashB[i])
                    distance++;
            }

            return distance;
        }

        public static double ComputeImageDifferenceScore(byte[] pngA, byte[] pngB)
        {
            try
            {
                using var msA = new MemoryStream(pngA);
                using var msB = new MemoryStream(pngB);
                using var bmpA = new Bitmap(msA);
                using var bmpB = new Bitmap(msB);

                var w = Math.Min(bmpA.Width, bmpB.Width);
                var h = Math.Min(bmpA.Height, bmpB.Height);
                if (w < 8 || h < 8)
                    return 100;

                var stepX = Math.Max(4, w / 24);
                var stepY = Math.Max(4, h / 24);
                double sum = 0;
                var samples = 0;

                for (var y = 0; y < h; y += stepY)
                {
                    for (var x = 0; x < w; x += stepX)
                    {
                        var a = bmpA.GetPixel(Math.Min(x, bmpA.Width - 1), Math.Min(y, bmpA.Height - 1));
                        var b = bmpB.GetPixel(Math.Min(x, bmpB.Width - 1), Math.Min(y, bmpB.Height - 1));
                        sum += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                        samples++;
                    }
                }

                return samples == 0 ? 100 : sum / (samples * 3.0);
            }
            catch
            {
                return 100;
            }
        }

        private static string ComputeDifferenceHash(byte[] pngBytes)
        {
            try
            {
                using var ms = new MemoryStream(pngBytes);
                using var source = new Bitmap(ms);
                using var resized = new Bitmap(HashWidth, HashHeight, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(source, 0, 0, HashWidth, HashHeight);
                }

                var luma = new double[HashWidth, HashHeight];
                for (var y = 0; y < HashHeight; y++)
                {
                    for (var x = 0; x < HashWidth; x++)
                    {
                        var p = resized.GetPixel(x, y);
                        luma[x, y] = p.R * 0.299 + p.G * 0.587 + p.B * 0.114;
                    }
                }

                var sb = new StringBuilder(HashWidth * (HashHeight - 1));
                for (var y = 0; y < HashHeight; y++)
                {
                    for (var x = 0; x < HashWidth - 1; x++)
                        sb.Append(luma[x, y] > luma[x + 1, y] ? '1' : '0');
                }

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildOcrKeywords(ScreenVisualContext? visualContext)
        {
            if (visualContext == null)
                return string.Empty;

            var parts = new[]
            {
                visualContext.OcrWindowTitle,
                visualContext.SelectedTreeNode,
                visualContext.SelectedListItem,
                visualContext.OcrStatusBar,
                visualContext.VisibleDialogTitle
            };

            return Normalize(string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))));
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToLowerInvariant();
        }
    }
}
