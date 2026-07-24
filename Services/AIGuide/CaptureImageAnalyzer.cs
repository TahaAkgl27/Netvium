using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class CaptureImageAnalyzer
    {
        public static bool IsMostlyBlank(byte[] pngBytes) => Analyze(pngBytes).IsBlank;

        public static BlankDetectionResult Analyze(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return new BlankDetectionResult
                {
                    IsBlank = true,
                    BlankScore = 100,
                    Reason = "empty-bytes"
                };
            }

            try
            {
                using var ms = new MemoryStream(pngBytes);
                using var bitmap = new Bitmap(ms);
                return Analyze(bitmap);
            }
            catch
            {
                return new BlankDetectionResult
                {
                    IsBlank = true,
                    BlankScore = 100,
                    Reason = "decode-failed"
                };
            }
        }

        public static BlankDetectionResult Analyze(Bitmap bitmap)
        {
            if (bitmap.Width < 8 || bitmap.Height < 8)
            {
                return new BlankDetectionResult
                {
                    IsBlank = true,
                    BlankScore = 100,
                    Reason = "too-small"
                };
            }

            var dark = 0;
            var white = 0;
            var transparent = 0;
            var samples = 0;
            double sumLuma = 0;
            double sumLumaSq = 0;

            var stepX = Math.Max(4, bitmap.Width / 32);
            var stepY = Math.Max(4, bitmap.Height / 32);

            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                for (var x = 0; x < bitmap.Width; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    samples++;

                    if (pixel.A < 16)
                    {
                        transparent++;
                        continue;
                    }

                    var luma = pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114;
                    sumLuma += luma;
                    sumLumaSq += luma * luma;

                    if (pixel.R < 24 && pixel.G < 24 && pixel.B < 24)
                        dark++;

                    if (pixel.R > 231 && pixel.G > 231 && pixel.B > 231)
                        white++;
                }
            }

            if (samples == 0)
            {
                return new BlankDetectionResult
                {
                    IsBlank = true,
                    BlankScore = 100,
                    Reason = "no-samples"
                };
            }

            var darkPercent = (double)dark / samples * 100.0;
            var whitePercent = (double)white / samples * 100.0;
            var transparentPercent = (double)transparent / samples * 100.0;
            var opaqueSamples = Math.Max(1, samples - transparent);
            var mean = sumLuma / opaqueSamples;
            var variance = Math.Max(0, sumLumaSq / opaqueSamples - mean * mean);
            var stdDev = Math.Sqrt(variance);
            var varianceScore = Math.Clamp(100.0 - stdDev * 2.5, 0, 100);

            var blankScore = Math.Max(
                Math.Max(darkPercent, whitePercent),
                Math.Max(transparentPercent, varianceScore));

            string reason = "ok";
            var isBlank = false;

            if (transparentPercent >= 85)
            {
                isBlank = true;
                reason = "mostly-transparent";
            }
            else if (darkPercent >= 90)
            {
                isBlank = true;
                reason = "mostly-black";
            }
            else if (whitePercent >= 90)
            {
                isBlank = true;
                reason = "mostly-white";
            }
            else if (varianceScore >= 92 && (darkPercent >= 70 || whitePercent >= 70))
            {
                isBlank = true;
                reason = "low-variance";
            }
            else if (stdDev < 6 && opaqueSamples > 8)
            {
                isBlank = true;
                reason = "uniform-color";
                blankScore = Math.Max(blankScore, 95);
            }

            return new BlankDetectionResult
            {
                IsBlank = isBlank,
                BlankScore = blankScore,
                Reason = reason,
                DarkPercent = darkPercent,
                WhitePercent = whitePercent,
                TransparentPercent = transparentPercent,
                VarianceScore = varianceScore
            };
        }

        public static byte[] EncodePng(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        public static double MeasureDarkPercentage(byte[] pngBytes) => Analyze(pngBytes).DarkPercent;

        public static double MeasureWhitePercentage(byte[] pngBytes) => Analyze(pngBytes).WhitePercent;
    }
}
