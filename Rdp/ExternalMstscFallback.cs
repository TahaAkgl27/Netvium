using System;
using System.Diagnostics;
using System.IO;

namespace Netvium.Rdp
{
    public static class ExternalMstscFallback
    {
        private static Action<string>? _logger;
        public static void SetLogger(Action<string> logger) => _logger = logger;
        public static void Log(string message) => _logger?.Invoke($"[RDP_EXTERNAL] {message}");

        public static void LaunchDirect(string host, int port)
        {
            var target = port <= 0 || port == 3389 ? host : $"{host}:{port}";
            Log($"Launching external mstsc.exe /v:{target}");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe"),
                Arguments = $"/v:{target}",
                UseShellExecute = true
            });
        }
    }
}