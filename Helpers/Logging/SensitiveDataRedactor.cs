using System.Text.RegularExpressions;

namespace Netvium.Helpers.Logging
{
    internal static class SensitiveDataRedactor
    {
        private const string Redacted = "[REDACTED]";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        private static readonly Regex ConnectionStringPattern = new(
            @"\b(?:Server|Data\s+Source|Host)\s*=\s*[^;\r\n]+(?:\s*;\s*[^;=\r\n]+\s*=\s*[^;\r\n]*){2,}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex JsonSecretPattern = new(
            @"(?<prefix>[""'](?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|token|connectionstring|connection_string)[""']\s*:\s*)[""'][^""'\r\n]*[""']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex SecretAssignmentPattern = new(
            @"(?<prefix>\b(?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|token|authorization|connectionstring|connection_string)\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s;,|&]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex IpAddressPattern = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[0-9a-f]{0,4}(?::[0-9a-f]{0,4}){2,7}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex BearerOrJwtPattern = new(
            @"\bBearer\s+[A-Za-z0-9._~+/=-]+|\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}(?:\.[A-Za-z0-9_-]{10,})?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        public static string RedactForPrompt(string? value)
        {
            var redacted = Redact(value);
            return string.IsNullOrWhiteSpace(redacted) ? redacted : IpAddressPattern.Replace(redacted, "[IP redacted]");
        }

        public static string WrapUntrusted(string? value)
        {
            var redacted = RedactForPrompt(value);
            return "<UNTRUSTED_DATA>\n" + redacted + "\n</UNTRUSTED_DATA>";
        }
        public static string Redact(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            try
            {
                var redacted = ConnectionStringPattern.Replace(value, Redacted);
                redacted = BearerOrJwtPattern.Replace(redacted, Redacted);
                redacted = JsonSecretPattern.Replace(redacted, match =>
                    match.Groups["prefix"].Value + $@"""{Redacted}""");
                redacted = SecretAssignmentPattern.Replace(redacted, match =>
                    match.Groups["prefix"].Value + Redacted);
                return redacted;
            }
            catch (RegexMatchTimeoutException)
            {
                return "[REDACTION TIMEOUT]";
            }
        }
    }
}
