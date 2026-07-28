using System.Text.RegularExpressions;

namespace Netvium.Services.AIGuide
{
    internal static class AIGuideSecretRedactor
    {
        private const string Redacted = "[REDACTED]";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Regex PemPrivateKeyPattern = new(
            @"-----BEGIN (?<type>[A-Z0-9 ]*PRIVATE KEY)-----[\s\S]*?-----END \k<type>-----",
            Options, RegexTimeout);
        private static readonly Regex AwsAccessKeyPattern = new(
            @"\b(?:(?:AKIA|ASIA|AIDA|AROA|AIPA|ANPA|ANVA)[A-Z0-9]{16}|A3T[A-Z0-9]{17})\b",
            Options, RegexTimeout);
        private static readonly Regex KeyValueSecretPattern = new(
            @"(?<prefix>\b(?:password|passwd|pwd|secret|api[_-]?key|token|activation[_ -]?code|license[_ -]?key|aws[_-]?secret[_-]?access[_-]?key|secretaccesskey)\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s;,|&]+)",
            Options, RegexTimeout);
        private static readonly Regex ConnectionStringPattern = new(
            @"\b(?:Server|Data Source)\s*=\s*[^;\r\n]+(?:;\s*[^;\r\n=]+\s*=\s*[^;\r\n]*){2,}",
            Options, RegexTimeout);
        private static readonly Regex IpAddressPattern = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[0-9a-f]{0,4}(?::[0-9a-f]{0,4}){2,7}\b",
            Options, RegexTimeout);
        private static readonly Regex BearerOrJwtPattern = new(
            @"\bBearer\s+[A-Za-z0-9._~+/=-]+|\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}(?:\.[A-Za-z0-9_-]{10,})?",
            Options, RegexTimeout);
        private static readonly Regex EmailPattern = new(
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", Options, RegexTimeout);
        private static readonly Regex GuidPattern = new(
            @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", Options, RegexTimeout);
        private static readonly Regex ActivationCodePattern = new(
            @"\b(?:[A-Z0-9]{4,6}-){3,5}[A-Z0-9]{4,6}\b", Options, RegexTimeout);
        private static readonly Regex DomainUserPattern = new(
            @"\b[A-Z][A-Z0-9._-]{1,62}\\[A-Za-z0-9][A-Za-z0-9._-]{0,63}\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex ContextualHostPattern = new(
            @"(?<prefix>\b(?:hostname|host_name|computername|computer_name|server_name|servername)\s*[:=]\s*)(?:""[^""\r\n]+""|'[^'\r\n]+'|[A-Z0-9][A-Z0-9._-]{0,252})",
            Options, RegexTimeout);

        public static string Redact(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            try
            {
                var redacted = PemPrivateKeyPattern.Replace(value, "[private key redacted]");
                redacted = ConnectionStringPattern.Replace(redacted, "[connection string redacted]");
                redacted = BearerOrJwtPattern.Replace(redacted, "[token redacted]");
                redacted = AwsAccessKeyPattern.Replace(redacted, "[AWS access key redacted]");
                redacted = KeyValueSecretPattern.Replace(redacted, m => m.Groups["prefix"].Value + "***");
                redacted = DomainUserPattern.Replace(redacted, "[domain user redacted]");
                redacted = ContextualHostPattern.Replace(redacted, m => m.Groups["prefix"].Value + "[hostname redacted]");
                redacted = EmailPattern.Replace(redacted, "[email redacted]");
                redacted = GuidPattern.Replace(redacted, "[identifier redacted]");
                redacted = ActivationCodePattern.Replace(redacted, "[activation code redacted]");
                redacted = IpAddressPattern.Replace(redacted, "[IP redacted]");
                return redacted;
            }
            catch (RegexMatchTimeoutException)
            {
                return "[REDACTION TIMEOUT]";
            }
        }

        public static string RedactUntrusted(string? value) =>
            "<UNTRUSTED_DATA>\n" + Redact(value) + "\n</UNTRUSTED_DATA>";

        public static bool ContainsSecretPattern(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                return PemPrivateKeyPattern.IsMatch(value) || AwsAccessKeyPattern.IsMatch(value)
                    || KeyValueSecretPattern.IsMatch(value) || ConnectionStringPattern.IsMatch(value)
                    || BearerOrJwtPattern.IsMatch(value) || EmailPattern.IsMatch(value)
                    || GuidPattern.IsMatch(value) || ActivationCodePattern.IsMatch(value)
                    || DomainUserPattern.IsMatch(value) || ContextualHostPattern.IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }
    }
}
