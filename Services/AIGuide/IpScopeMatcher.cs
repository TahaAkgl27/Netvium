using System.Net;
using System.Text.RegularExpressions;

namespace Netvium.Services.AIGuide
{
    internal static class IpScopeMatcher
    {
        private static readonly Regex IpPattern = new(
            @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string? ExtractIpFromQuestion(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return null;

            var match = IpPattern.Match(question);
            if (!match.Success)
                return null;

            return IPAddress.TryParse(match.Groups[1].Value, out _) ? match.Groups[1].Value : null;
        }
    }
}
