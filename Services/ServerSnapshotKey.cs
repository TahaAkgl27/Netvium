using System.Collections.Generic;
using Netvium.Models;

namespace Netvium.Services
{
    /// <summary>
    /// Stable, normalized snapshot key — same server always maps to the same .nsnap hash after restart.
    /// </summary>
    public static class ServerSnapshotKey
    {
        public static string FromServer(ServerInfo? server)
        {
            if (server == null) return "unknown";
            var host = NormalizeHost(server.Ip);
            var user = NormalizeUsername(server.Username);
            var port = server.Port <= 0 ? 3389 : server.Port;
            return $"{host}:{port}:{user}";
        }

        /// <summary>
        /// Legacy connections.json embedded snapshot key (ip:port only — no username).
        /// Differs from <see cref="FromServer"/> (host:port:user). Load/save must try both.
        /// </summary>
        public static string ConnectionHistoryKey(ServerInfo server)
        {
            var host = NormalizeHost(server.Ip);
            var port = server.Port <= 0 ? 3389 : server.Port;
            return $"{host}:{port}";
        }

        public static string LegacyCacheKey(ServerInfo server) =>
            $"{NormalizeHost(server.Ip)}|{NormalizeUsername(server.Username)}";

        /// <summary>Canonical snapshot key first, then legacy connections.json key.</summary>
        public static IEnumerable<string> HistoryLookupKeys(ServerInfo server)
        {
            yield return FromServer(server);
            var legacy = ConnectionHistoryKey(server);
            if (!string.Equals(legacy, FromServer(server), StringComparison.OrdinalIgnoreCase))
                yield return legacy;
        }

        public static string NormalizeHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return "unknown";
            return host.Trim().ToLowerInvariant();
        }

        public static string NormalizeUsername(string? username)
        {
            if (string.IsNullOrWhiteSpace(username)) return "anon";
            return username.Trim().ToLowerInvariant();
        }
    }
}
