using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Netvium.Helpers;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Windows built-in OCR for captured screenshot regions. Runs off the UI thread.
    /// </summary>
    internal static class ScreenOcrEngine
    {
        private static OcrEngine? _engine;

        public static async Task<OcrRegionResult> RecognizeRegionAsync(
            byte[] pngBytes,
            int imageWidth,
            int imageHeight,
            double xPercent,
            double yPercent,
            double widthPercent,
            double heightPercent,
            CancellationToken ct = default)
        {
            if (pngBytes.Length == 0 || imageWidth <= 0 || imageHeight <= 0)
                return new OcrRegionResult(string.Empty, 0);

            try
            {
                var bounds = ComputeBounds(imageWidth, imageHeight, xPercent, yPercent, widthPercent, heightPercent);
                var cropped = CropPng(pngBytes, bounds);
                if (cropped.Length == 0)
                    return new OcrRegionResult(string.Empty, 0);

                ct.ThrowIfCancellationRequested();
                var (text, lineCount, wordCount) = await RecognizePngAsync(cropped, ct).ConfigureAwait(false);
                var confidence = EstimateConfidence(text, bounds.Width, bounds.Height, lineCount, wordCount);
                return new OcrRegionResult(text, confidence);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"OCR region failed: {ex.Message}", category: "AIGuide", action: "OcrRegion");
                return new OcrRegionResult(string.Empty, 0);
            }
        }

        private static Rectangle ComputeBounds(
            int imageWidth,
            int imageHeight,
            double xPercent,
            double yPercent,
            double widthPercent,
            double heightPercent)
        {
            var x = Math.Clamp((int)Math.Round(imageWidth * xPercent), 0, Math.Max(0, imageWidth - 1));
            var y = Math.Clamp((int)Math.Round(imageHeight * yPercent), 0, Math.Max(0, imageHeight - 1));
            var w = Math.Clamp((int)Math.Round(imageWidth * widthPercent), 1, imageWidth - x);
            var h = Math.Clamp((int)Math.Round(imageHeight * heightPercent), 1, imageHeight - y);
            return new Rectangle(x, y, w, h);
        }

        private static byte[] CropPng(byte[] pngBytes, Rectangle bounds)
        {
            using var input = new MemoryStream(pngBytes);
            using var image = Image.FromStream(input);
            using var cropped = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(image, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
            }

            using var output = new MemoryStream();
            cropped.Save(output, ImageFormat.Png);
            return output.ToArray();
        }

        private static async Task<(string Text, int LineCount, int WordCount)> RecognizePngAsync(
            byte[] pngBytes,
            CancellationToken ct)
        {
            var engine = GetOrCreateEngine();
            if (engine == null)
                return (string.Empty, 0, 0);

            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(pngBytes.AsBuffer()).AsTask(ct).ConfigureAwait(false);
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            var softwareBitmap = await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask(ct)
                .ConfigureAwait(false);

            var result = await engine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
            if (result == null)
                return (string.Empty, 0, 0);

            var lineCount = result.Lines.Count;
            var wordCount = result.Lines.Sum(l => l.Words.Count);
            return (result.Text?.Trim() ?? string.Empty, lineCount, wordCount);
        }

        private static OcrEngine? GetOrCreateEngine()
        {
            if (_engine != null)
                return _engine;

            _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                      ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                      ?? OcrEngine.TryCreateFromLanguage(new Language("tr-TR"));
            return _engine;
        }

        private static double EstimateConfidence(string text, int regionWidth, int regionHeight, int lineCount, int wordCount)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0.15;

            var area = Math.Max(1, regionWidth * regionHeight);
            var density = (text.Length * 1000.0) / area;
            var score = 0.35;

            if (text.Length >= 4)
                score += 0.15;
            if (text.Length >= 12)
                score += 0.1;
            if (lineCount > 0)
                score += 0.1;
            if (wordCount >= 2)
                score += 0.1;
            if (density is > 0.02 and < 2.5)
                score += 0.1;

            if (text.Contains('�', StringComparison.Ordinal))
                score -= 0.2;

            return Math.Clamp(score, 0.1, 0.92);
        }
    }
}
