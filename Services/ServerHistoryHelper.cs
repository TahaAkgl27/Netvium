using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Netvium.Models;

namespace Netvium.Services
{
    /// <summary>
    /// Resolves server history across canonical (host:port:user) and legacy (host:port) keys.
    /// </summary>
    internal static class ServerHistoryHelper
    {
        public static bool TryGetHistory(
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            ServerInfo server,
            out List<WmiSnapshot> history)
        {
            foreach (var key in ServerSnapshotKey.HistoryLookupKeys(server))
            {
                if (serverHistory.TryGetValue(key, out history!) && history.Count > 0)
                    return true;
            }

            history = serverHistory.GetOrAdd(ServerSnapshotKey.FromServer(server), _ => new List<WmiSnapshot>());
            return history.Count > 0;
        }

        public static List<WmiSnapshot> GetOrCreateHistory(
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            ServerInfo server)
        {
            var canonicalKey = ServerSnapshotKey.FromServer(server);
            foreach (var key in ServerSnapshotKey.HistoryLookupKeys(server))
            {
                if (!serverHistory.TryGetValue(key, out var existing) || existing.Count == 0)
                    continue;

                if (string.Equals(key, canonicalKey, StringComparison.OrdinalIgnoreCase))
                    return existing;

                var migrated = serverHistory.GetOrAdd(canonicalKey, _ => new List<WmiSnapshot>());
                lock (migrated)
                {
                    if (migrated.Count == 0)
                        migrated.AddRange(existing);
                }

                return migrated;
            }

            return serverHistory.GetOrAdd(canonicalKey, _ => new List<WmiSnapshot>());
        }
    }
}
