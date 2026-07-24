using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Netvium.Models;

namespace Netvium.Helpers
{
    /// <summary>
    /// Builds live system-status components from WMI service state and lightweight probes.
    /// </summary>
    public static class SystemStatusProbe
    {
        public static List<DetectedComponent> BuildComponents(WmiSnapshot snap, ManagementScope? scope = null)
        {
            var list = new List<DetectedComponent>();
            if (snap == null || !snap.Connected)
                return list;

            var services = snap.Services ?? new List<ServiceRow>();
            string Svc(string name) =>
                services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.State ?? "Unknown";

            bool running(string name) =>
                Svc(name).Equals("Running", StringComparison.OrdinalIgnoreCase);

            list.Add(Make("Firewall", "Security", running("mpssvc") ? "Aktif" : "Kapalı", running("mpssvc") ? "🛡️" : "⚠️"));
            list.Add(Make("Windows Defender", "Security", running("WinDefend") || running("MsMpSvc") ? "Aktif" : "Pasif",
                running("WinDefend") || running("MsMpSvc") ? "🦠" : "⚠️"));

            var bitLocker = ResolveBitLockerStatus(snap, scope);
            list.Add(Make("BitLocker", "Security", bitLocker, bitLocker.Contains("Aktif") ? "🔒" : "🔓"));

            list.Add(Make("Windows Update", "Maintenance", running("wuauserv") ? "Aktif" : "Durdurulmuş",
                running("wuauserv") ? "🔄" : "⚠️"));

            var pendingRestart = ResolvePendingRestartStatus(snap, scope);
            list.Add(Make("Pending Restart", "Maintenance", pendingRestart, pendingRestart.Contains("Gerekli") ? "⚠️" : "✅"));

            list.Add(Make("Remote Registry", "Service", running("RemoteRegistry") ? "Aktif" : "Kapalı",
                running("RemoteRegistry") ? "⚙️" : "🔒"));
            list.Add(Make("SMB", "Network", running("LanmanServer") ? "Aktif" : "Kapalı",
                running("LanmanServer") ? "📁" : "⚠️"));
            list.Add(Make("DNS", "Network", running("Dnscache") || running("DNS") ? "Aktif" : "Pasif", "🌐"));
            list.Add(Make("DHCP", "Network", running("Dhcp") ? "Aktif" : "Pasif", "📡"));
            list.Add(Make("Time Sync", "Service", running("W32Time") ? "Senkron" : "Uyarı", running("W32Time") ? "🕐" : "⚠️"));

            return list;
        }

        public static void EnrichSnapshot(WmiSnapshot snap, string ip, string username, string password)
        {
            try
            {
                var scope = WmiCollector.CreateManagementScope(ip, username, password);
                scope.Connect();
                if (!scope.IsConnected)
                    return;

                var components = BuildComponents(snap, scope);
                snap.SystemStatusComponents = components;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"System status probe failed: {ex.Message}", category: "WMI", action: "SystemStatus");
                snap.SystemStatusComponents = BuildComponents(snap);
            }
        }

        private static DetectedComponent Make(string name, string type, string status, string icon) =>
            new(name, type, status, icon);

        private static string ResolveBitLockerStatus(WmiSnapshot snap, ManagementScope? scope)
        {
            if (snap.BitLockerVolumes?.Count > 0)
            {
                if (snap.BitLockerVolumes.Any(v => v.ProtectionStatus.Equals("On", StringComparison.OrdinalIgnoreCase)))
                    return "Aktif (Şifreli)";
                if (snap.BitLockerVolumes.Any(v => v.ProtectionStatus.Equals("Off", StringComparison.OrdinalIgnoreCase)))
                    return "Kapalı";
                return "Bilinmiyor";
            }

            return ProbeBitLocker(scope);
        }

        private static string ResolvePendingRestartStatus(WmiSnapshot snap, ManagementScope? scope)
        {
            if (snap.WindowsUpdateRebootRequired.HasValue)
                return snap.WindowsUpdateRebootRequired.Value ? "Yeniden başlatma gerekli" : "Gerekli değil";

            return ProbePendingRestart(scope);
        }

        private static string ProbeBitLocker(ManagementScope? scope)
        {
            if (scope == null || !scope.IsConnected)
                return "Bilinmiyor";

            try
            {
                using var searcher = WmiCollector.CreateTimedSearcher(scope,
                    new ObjectQuery("SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='C:'"));
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    var status = Convert.ToInt32(obj["ProtectionStatus"]);
                    return status switch
                    {
                        1 => "Aktif (Şifreli)",
                        0 => "Kapalı",
                        2 => "Şifreleme devam ediyor",
                        _ => $"Durum {status}"
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"BitLocker probe failed: {ex.Message}", category: "WMI", action: "BitLocker");
            }

            return "Desteklenmiyor";
        }

        private static string ProbePendingRestart(ManagementScope? scope)
        {
            if (scope == null || !scope.IsConnected)
                return "Bilinmiyor";

            try
            {
                using var searcher = WmiCollector.CreateTimedSearcher(scope,
                    new ObjectQuery("SELECT RebootRequired FROM Win32_ComputerSystem"));
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    if (obj["RebootRequired"] is bool required && required)
                        return "Yeniden başlatma gerekli";
                }

                using var osSearcher = WmiCollector.CreateTimedSearcher(scope,
                    new ObjectQuery("SELECT Caption FROM Win32_OperatingSystem"));
                using var osResults = osSearcher.Get();
                foreach (ManagementObject obj in osResults)
                {
                    _ = obj["Caption"];
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Pending restart probe failed: {ex.Message}", category: "WMI", action: "RebootPending");
            }

            return "Gerekli değil";
        }
    }
}
