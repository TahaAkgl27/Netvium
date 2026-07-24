using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ScreenChangeDetector
    {
        private const int HashHammingThreshold = 12;
        private const double ImageDifferenceThreshold = 18.0;

        public static ScreenChangeResult Compare(
            ScreenFingerprint? previous,
            ScreenFingerprint current,
            byte[]? previousImageBytes = null,
            byte[]? currentImageBytes = null)
        {
            if (previous == null)
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "first-capture"
                };
            }

            var hamming = ScreenFingerprintBuilder.HammingDistance(previous.PerceptualHash, current.PerceptualHash);
            var imageDiff = previousImageBytes != null && currentImageBytes != null
                ? ScreenFingerprintBuilder.ComputeImageDifferenceScore(previousImageBytes, currentImageBytes)
                : 0;

            if (!string.Equals(previous.ApplicationFingerprint, current.ApplicationFingerprint, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(current.ApplicationFingerprint))
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "application-fingerprint-changed",
                    HashHammingDistance = hamming,
                    ImageDifferenceScore = imageDiff
                };
            }

            if (!string.Equals(previous.DetectedApplication, current.DetectedApplication, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(previous.DetectedApplication) || !string.IsNullOrWhiteSpace(current.DetectedApplication)))
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "detected-application-changed",
                    HashHammingDistance = hamming,
                    ImageDifferenceScore = imageDiff
                };
            }

            if (!string.Equals(previous.ActiveWindowTitle, current.ActiveWindowTitle, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(previous.ActiveWindowTitle) || !string.IsNullOrWhiteSpace(current.ActiveWindowTitle)))
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "window-title-changed",
                    HashHammingDistance = hamming,
                    ImageDifferenceScore = imageDiff
                };
            }

            if (hamming >= HashHammingThreshold)
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "perceptual-hash-changed",
                    HashHammingDistance = hamming,
                    ImageDifferenceScore = imageDiff
                };
            }

            if (imageDiff >= ImageDifferenceThreshold)
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "image-difference-threshold",
                    HashHammingDistance = hamming,
                    ImageDifferenceScore = imageDiff
                };
            }

            if (!string.Equals(previous.OcrKeywords, current.OcrKeywords, StringComparison.OrdinalIgnoreCase)
                && KeywordChangeIsSignificant(previous.OcrKeywords, current.OcrKeywords))
            {
                return new ScreenChangeResult
                {
                    IsNewScreen = true,
                    Reason = "ocr-keywords-changed",
                    HashHammingDistance = hamming,
                    ImageDifferenceScore = imageDiff
                };
            }

            return new ScreenChangeResult
            {
                IsNewScreen = false,
                Reason = "unchanged",
                HashHammingDistance = hamming,
                ImageDifferenceScore = imageDiff
            };
        }

        private static bool KeywordChangeIsSignificant(string previous, string current)
        {
            if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
                return false;

            var prevTokens = previous.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var curTokens = current.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (prevTokens.Length == 0 || curTokens.Length == 0)
                return false;

            var overlap = prevTokens.Intersect(curTokens, StringComparer.OrdinalIgnoreCase).Count();
            var union = prevTokens.Union(curTokens, StringComparer.OrdinalIgnoreCase).Count();
            if (union == 0)
                return false;

            return (double)overlap / union < 0.45;
        }
    }
}
