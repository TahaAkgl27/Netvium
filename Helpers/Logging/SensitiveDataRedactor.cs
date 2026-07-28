using System.Text.RegularExpressions;

namespace Netvium.Helpers.Logging
{
    internal static class SensitiveDataRedactor
    {
        private const string Redacted = "[REDACTED]";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Regex PemPrivateKeyPattern = new(
            @"-----BEGIN (?<type>[A-Z0-9 ]*PRIVATE KEY)-----[\s\S]*?-----END \k<type>-----", Options, RegexTimeout);
        private static readonly Regex AwsAccessKeyPattern = new(
            @"\b(?:(?:AKIA|ASIA|AIDA|AROA|AIPA|ANPA|ANVA)[A-Z0-9]{16}|A3T[A-Z0-9]{17})\b", Options, RegexTimeout);
        private static readonly Regex ConnectionStringPattern = new(
            @"\b(?:Server|Data\s+Source|Host)\s*=\s*[^;\r\n]+(?:\s*;\s*[^;=\r\n]+\s*=\s*[^;\r\n]*){2,}", Options, RegexTimeout);
        private static readonly Regex JsonSecretPattern = new(
            @"(?<prefix>[""'](?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|token|connectionstring|connection_string|aws[_-]?secret[_-]?access[_-]?key|secretaccesskey)[""']\s*:\s*)[""'][^""'\r\n]*[""']",
            Options, RegexTimeout);
        private static readonly Regex SecretAssignmentPattern = new(
            @"(?<prefix>\b(?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|token|authorization|connectionstring|connection_string|aws[_-]?secret[_-]?access[_-]?key|secretaccesskey)\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s;,|&]+)",
            Options, RegexTimeout);
        private static readonly Regex DomainUserPattern = new(
            @"\b[A-Z][A-Z0-9._-]{1,62}\\[A-Za-z0-9][A-Za-z0-9._-]{0,63}\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex ContextualHostPattern = new(
            @"(?<prefix>\b(?:hostname|host_name|computername|computer_name|server_name|servername)\s*[:=]\s*)(?:""[^""\r\n]+""|'[^'\r\n]+'|[A-Z0-9][A-Z0-9._-]{0,252})",
            Options, RegexTimeout);
        private static readonly Regex IpAddressPattern = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[0-9a-f]{0,4}(?::[0-9a-f]{0,4}){2,7}\b", Options, RegexTimeout);
        private static readonly Regex BearerOrJwtPattern = new(
            @"\bBearer\s+[A-Za-z0-9._~+/=-]+|\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}(?:\.[A-Za-z0-9_-]{10,})?", Options, RegexTimeout);

        public static string RedactForPrompt(string? value)
        {
            var redacted = Redact(value);
            if (string.IsNullOrWhiteSpace(redacted)) return redacted;
            try { return IpAddressPattern.Replace(redacted, "[IP redacted]"); }
            catch (RegexMatchTimeoutException) { return "[REDACTION TIMEOUT]"; }
        }

        public static string WrapUntrusted(string? value) =>
            "<UNTRUSTED_DATA>\n" + RedactForPrompt(value) + "\n</UNTRUSTED_DATA>";

        public static string Redact(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            try
            {
                var redacted = PemPrivateKeyPattern.Replace(value, "[PRIVATE KEY REDACTED]");
                redacted = ConnectionStringPattern.Replace(redacted, Redacted);
                redacted = BearerOrJwtPattern.Replace(redacted, Redacted);
                redacted = AwsAccessKeyPattern.Replace(redacted, "[AWS ACCESS KEY REDACTED]");
                redacted = JsonSecretPattern.Replace(redacted, m => m.Groups["prefix"].Value + $@"""{Redacted}""");
                redacted = SecretAssignmentPattern.Replace(redacted, m => m.Groups["prefix"].Value + Redacted);
                redacted = DomainUserPattern.Replace(redacted, "[DOMAIN USER REDACTED]");
                redacted = ContextualHostPattern.Replace(redacted, m => m.Groups["prefix"].Value + "[HOSTNAME REDACTED]");
                return redacted;
            }
            catch (RegexMatchTimeoutException)
            {
                return "[REDACTION TIMEOUT]";
            }
        }
    }
}