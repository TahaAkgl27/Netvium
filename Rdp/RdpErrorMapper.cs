using System.Collections.Generic;

namespace Netvium.Rdp
{
    public static class RdpErrorMapper
    {
        private static readonly Dictionary<int, string> _reasons = new()
        {
            { 1, "Local disconnect" },
            { 2, "Remote disconnect (User initiated)" },
            { 3, "Network interruption" },
            { 4, "Bad credentials" },
            { 5, "NLA Required" },
            { 263, "Protocol error" },
            { 516, "Access denied" },
            { 1025, "Licensing error" },
            { 1281, "Encryption error" },
            { 2308, "Socket closed" },
            { 3334, "Internal error" }
        };

        public static string GetFriendlyReason(int reason)
        {
            if (_reasons.TryGetValue(reason, out var msg)) return msg;
            return $"Unknown Error ({reason})";
        }
    }
}
