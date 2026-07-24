using System;

namespace Netvium.Rdp
{
    public static class RdpCredentialHelper
    {
        /// <summary>
        /// Optional enterprise default domain from user settings — never inferred from server display name.
        /// </summary>
        public const string DefaultDomain = "";

        /// <summary>
        /// Parses explicit DOMAIN\user or UPN (user@domain). Never infers domain from server name or labels.
        /// </summary>
        public static (string NormalizedUser, string Domain, string User) NormalizeUsername(string username, string? configuredDefaultDomain = null)
        {
            username = (username ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(username))
                return (string.Empty, string.Empty, string.Empty);

            if (username.Contains('\\', StringComparison.Ordinal))
            {
                var parts = username.Split('\\', 2);
                var dom = parts[0].Trim();
                var user = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                return ($"{dom}\\{user}", dom, user);
            }

            if (username.Contains('@', StringComparison.Ordinal))
            {
                // UPN — OCX Domain must remain empty; UserName receives full UPN.
                return (username, string.Empty, username);
            }

            var defaultDomain = (configuredDefaultDomain ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(defaultDomain))
                return ($"{defaultDomain}\\{username}", defaultDomain, username);

            return (username, string.Empty, username);
        }
    }
}
