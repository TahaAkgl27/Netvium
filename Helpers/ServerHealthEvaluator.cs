using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class ServerHealthEvaluator
    {
        public static (HealthBadge badge, string? reason) CalculateOverallHealth(long? pingMs, bool rdpOpen, WmiSnapshot? snap)
        {
            if (!pingMs.HasValue && !rdpOpen) return (HealthBadge.Offline, "Ping/RDP erişilemiyor");
            if (!rdpOpen) return (HealthBadge.Critical, "RDP portu kapalı");

            if (snap != null && snap.Connected)
            {
                if (snap.Services != null && snap.Services.Any())
                {
                    var stoppedService = snap.Services.FirstOrDefault(s =>
                        !s.State.Equals("Running", StringComparison.OrdinalIgnoreCase) &&
                        !s.State.Equals("Started", StringComparison.OrdinalIgnoreCase));

                    if (stoppedService != null)
                        return (HealthBadge.Critical, $"Servis durdu: {stoppedService.Display}");
                }

                if (snap.DiskUsedPct >= 90)
                    return (HealthBadge.Critical, $"Disk kritik (%{snap.DiskUsedPct:0})");

                if (snap.RamUsedPct >= 85)
                    return (HealthBadge.Warning, $"RAM yüksek (%{snap.RamUsedPct:0})");

                if (snap.CpuPct >= 90)
                    return (HealthBadge.Warning, $"CPU yüksek (%{snap.CpuPct})");

                if (pingMs.HasValue && pingMs.Value >= 150)
                    return (HealthBadge.Warning, $"Ping yüksek ({pingMs.Value}ms)");

                return (HealthBadge.Healthy, "Normal çalışıyor");
            }

            if (pingMs.HasValue || rdpOpen)
                return (HealthBadge.Warning, "WMI verisi yok, yalnızca ağ erişimi doğrulandı");

            return (HealthBadge.Offline, "Canlı veri yok");
        }

        public static (int healthy, int warning, int critical, int offline) CountStatuses(System.Collections.Generic.IEnumerable<HealthBadge> statuses)
        {
            int healthy = 0, warning = 0, critical = 0, offline = 0;
            foreach (var status in statuses)
            {
                switch (status)
                {
                    case HealthBadge.Healthy: healthy++; break;
                    case HealthBadge.Warning: warning++; break;
                    case HealthBadge.Critical: critical++; break;
                    case HealthBadge.Offline: offline++; break;
                }
            }

            // Dashboard offline'ı critical sayısına ekler
            critical += offline;
            return (healthy, warning, critical, offline);
        }
    }
}
