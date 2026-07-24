using System.Text.RegularExpressions;

namespace Netvium.Services.AIGuide
{
    internal static class AIGuideSecretRedactor
    {
        private static readonly Regex KeyValueSecretPattern = new(
            @"(password|passwd|pwd|secret|api[_-]?key|token|activation[_ -]?code|license[_ -]?key)\s*[:=]\s*[^\s;,]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ConnectionStringPattern = new(
            @"\b(?:Server|Data Source)\s*=\s*[^;\r\n]+(?:;\s*[^;\r\n=]+\s*=\s*[^;\r\n]*){2,}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex IpAddressPattern = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[0-9a-f]{0,4}(?::[0-9a-f]{0,4}){2,7}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BearerOrJwtPattern = new(
            @"\bBearer\s+[A-Za-z0-9._~+/=-]+|\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}(?:\.[A-Za-z0-9_-]{10,})?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EmailPattern = new(
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex GuidPattern = new(
            @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ActivationCodePattern = new(
            @"\b(?:[A-Z0-9]{4,6}-){3,5}[A-Z0-9]{4,6}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Redact(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;

            var redacted = ConnectionStringPattern.Replace(value, "[connection string redacted]");
            redacted = BearerOrJwtPattern.Replace(redacted, "[token redacted]");
            redacted = KeyValueSecretPattern.Replace(redacted, match =>
            {
                var separatorIndex = match.Value.IndexOfAny([':', '=']);
                var key = separatorIndex > 0 ? match.Value[..separatorIndex].TrimEnd() : "secret";
                var separator = separatorIndex >= 0 ? match.Value[separatorIndex] : ':';
                return $"{key}{separator} ***";
            });
            redacted = EmailPattern.Replace(redacted, "[email redacted]");
            redacted = GuidPattern.Replace(redacted, "[identifier redacted]");
            redacted = ActivationCodePattern.Replace(redacted, "[activation code redacted]");
            redacted = IpAddressPattern.Replace(redacted, "[IP redacted]");
            return redacted;
        }

        public static string RedactUntrusted(string? value)
        {
            var redacted = Redact(value);
            return "<UNTRUSTED_DATA>\n" + redacted + "\n</UNTRUSTED_DATA>";
        }
        public static bool ContainsSecretPattern(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (KeyValueSecretPattern.IsMatch(value)
                || ConnectionStringPattern.IsMatch(value)
                || BearerOrJwtPattern.IsMatch(value)
                || EmailPattern.IsMatch(value)
                || GuidPattern.IsMatch(value)
                || ActivationCodePattern.IsMatch(value));
    }
}
