using System;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class ConnectionSearchHelper
    {
        public static bool Matches(ServerInfo server, string folderName, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            var q = query.Trim();
            return Contains(server.Name, q)
                   || Contains(server.Ip, q)
                   || Contains(folderName, q)
                   || Contains(server.Username, q);
        }

        private static bool Contains(string? value, string query) =>
            !string.IsNullOrEmpty(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
