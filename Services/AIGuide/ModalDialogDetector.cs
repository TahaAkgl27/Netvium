using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ModalDialogDetector
    {
        private static readonly string[] ModalKeywords =
        [
            "warning", "alert", "uyarı", "uyari", "error", "hata", "check point", "checkpoint",
            "endpoint security", "windows defender", "defender", "blocked", "engellendi",
            "malicious", "zararlı", "zararli", "threat", "tehdit", "dns resolution", "dns çözümleme",
            "reset password", "confirm", "onay", "dialog", "are you sure", "emin misiniz"
        ];

        private static readonly string[] SecurityVendorKeywords =
        [
            "check point", "checkpoint", "endpoint security", "windows defender",
            "microsoft defender", "crowdstrike", "sentinelone", "sophos", "kaspersky"
        ];

        public static ModalDetectionResult Detect(string dialogOcr, string centerOcr, string titleOcr)
        {
            var combined = string.Join("\n", dialogOcr, centerOcr, titleOcr).Trim();
            if (string.IsNullOrWhiteSpace(combined))
                return ModalDetectionResult.None;

            var lower = combined.ToLowerInvariant();
            var hasModalKeyword = ModalKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
            var isSecurity = SecurityVendorKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));

            if (!hasModalKeyword && centerOcr.Length < 40)
                return ModalDetectionResult.None;

            var title = ExtractFirstLine(dialogOcr);
            if (string.IsNullOrWhiteSpace(title))
                title = ExtractFirstLine(centerOcr);

            var body = combined;
            var priority = isSecurity || lower.Contains("warning", StringComparison.Ordinal)
                || lower.Contains("uyarı", StringComparison.Ordinal)
                || lower.Contains("blocked", StringComparison.Ordinal);

            return new ModalDetectionResult
            {
                HasModal = hasModalKeyword || centerOcr.Length >= 40,
                HasModalPriority = priority,
                IsSecurityAlert = isSecurity,
                ModalTitle = title,
                ModalBodyText = Truncate(body, 500),
                FocusedContext = priority
                    ? $"MODAL/POPUP PRIORITY: {title}. {Truncate(body, 200)}"
                    : string.Empty
            };
        }

        private static string ExtractFirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.Length >= 3) ?? string.Empty;
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..(max - 3)] + "...";
    }

    internal sealed class ModalDetectionResult
    {
        public bool HasModal { get; init; }
        public bool HasModalPriority { get; init; }
        public bool IsSecurityAlert { get; init; }
        public string ModalTitle { get; init; } = string.Empty;
        public string ModalBodyText { get; init; } = string.Empty;
        public string FocusedContext { get; init; } = string.Empty;

        public static ModalDetectionResult None { get; } = new();
    }
}
