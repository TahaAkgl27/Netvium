using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Netvium.Helpers
{
    /// <summary>
    /// Validates host/IP strings before they are passed to external processes.
    /// </summary>
    public static class NetworkHostValidator
    {
        private const string ForbiddenChars = "&|;><%^\"'`\r\n\t ";

        // RFC 1123 hostname labels (simplified, no internationalized domain names).
        private static readonly Regex HostnameRegex = new(
            @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryValidatePingTarget(string? input, out string normalized, out string errorMessage)
        {
            normalized = (input ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                errorMessage = "Host or IP address is required.";
                return false;
            }

            if (normalized.Any(c => ForbiddenChars.Contains(c)))
            {
                errorMessage = "Host contains invalid characters.";
                return false;
            }

            if (IPAddress.TryParse(normalized, out _))
            {
                errorMessage = string.Empty;
                return true;
            }

            if (normalized.Length > 253 || !HostnameRegex.IsMatch(normalized))
            {
                errorMessage = "Enter a valid IP address or hostname.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static bool TryValidateAgentHost(string? input, out string normalized, out string errorMessage)
        {
            normalized = (input ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                errorMessage = "Agent host is required.";
                return false;
            }

            if (normalized.Any(c => ForbiddenChars.Contains(c)))
            {
                errorMessage = "Agent host contains invalid characters.";
                return false;
            }

            if (IPAddress.TryParse(normalized, out _))
            {
                errorMessage = string.Empty;
                return true;
            }

            if (normalized.Length > 253 || !HostnameRegex.IsMatch(normalized))
            {
                errorMessage = "Agent host must be a valid IP address or hostname.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
