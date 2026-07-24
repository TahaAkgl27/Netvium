using System;
using System.Threading;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class AgentEndpointHelper
    {
        private static int _httpWarningLogged;

        public static Uri? TryBuildMetricsUri(string serverHost, AppSettings settings) =>
            TryBuildUri(serverHost, settings, "metrics");

        public static Uri? TryBuildHealthUri(string serverHost, AppSettings settings) =>
            TryBuildUri(serverHost, settings, "health");

        public static Uri? TryBuildUri(string serverHost, AppSettings settings, string path)
        {
            if (!NetworkHostValidator.TryValidateAgentHost(serverHost, out var host, out _))
                return null;

            var scheme = NormalizeScheme(settings.AgentScheme);
            var port = settings.AgentPort > 0 ? settings.AgentPort : 9841;
            var resource = path.Trim().TrimStart('/');

            LogHttpWarningIfNeeded(scheme);

            return new Uri($"{scheme}://{host}:{port}/{resource}");
        }

        public static string NormalizeScheme(string? scheme)
        {
            if (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
                return "https";
            return "http";
        }

        public static void LogHttpWarningIfNeeded(string scheme)
        {
            if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                return;

            if (Interlocked.CompareExchange(ref _httpWarningLogged, 1, 0) != 0)
                return;

            AppLogger.Warning(
                "Agent communication uses unencrypted HTTP. Configure AgentScheme=https for production deployments.",
                category: "Agent",
                action: "Security");
        }
    }
}
