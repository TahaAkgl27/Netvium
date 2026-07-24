using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class ServerRuleEngine
    {
        public static List<AnalysisFinding> Evaluate(ServerAnalysisContext ctx)
        {
            var findings = new List<AnalysisFinding>();
            if (!ctx.HasWmiData)
            {
                findings.Add(Finding("wmi_missing", "WMI verisi alınamadı", "Yalnızca ağ erişimi doğrulandı; metrik ve servis listesi eksik.", InsightSeverity.Warning, "Data"));
                return findings;
            }

            if (ctx.DiskPercent is > 80)
                findings.Add(Finding("disk_high", $"Disk kullanımı %{ctx.DiskPercent:0}", $"Disk doluluğu %{ctx.DiskPercent:0} (eşik: >80%).", InsightSeverity.Critical, "Performance"));

            if (ctx.RamPercent is > 90)
                findings.Add(Finding("ram_high", $"RAM kullanımı %{ctx.RamPercent:0}", $"Bellek kullanımı %{ctx.RamPercent:0} (eşik: >90%).", InsightSeverity.Critical, "Performance"));

            if (ctx.CpuPercent is > 85)
                findings.Add(Finding("cpu_high", $"CPU kullanımı %{ctx.CpuPercent}", $"İşlemci %{ctx.CpuPercent} (eşik: >85%).", InsightSeverity.Warning, "Performance"));

            if (ctx.LatencyMs is > 50)
                findings.Add(Finding("latency_high", $"Latency {ctx.LatencyMs}ms", $"Ping gecikmesi {ctx.LatencyMs}ms (eşik: >50ms).", InsightSeverity.Warning, "Performance"));

            foreach (var svc in ctx.StoppedCriticalServices.Take(5))
            {
                findings.Add(Finding($"svc_{svc}", $"Kritik servis durdurulmuş: {svc}", $"Servis durumu: Stopped ({svc}).", InsightSeverity.Critical, "Availability"));
            }

            if (!ctx.FirewallRunning)
                findings.Add(Finding("firewall_off", "Windows Firewall devre dışı", "mpssvc (Windows Firewall) çalışmıyor.", InsightSeverity.Critical, "Security"));

            if (ctx.RdpOpen)
                findings.Add(Finding("rdp_open", "RDP portu erişilebilir", "TCP 3389 erişimi doğrulandı.", InsightSeverity.Info, "Security"));

            if (ctx.WinRmRunning)
                findings.Add(Finding("winrm_on", "WinRM aktif", "WinRM servisi Running durumda.", InsightSeverity.Info, "Security"));

            if (string.Equals(ctx.RemoteRegistryState, "Stopped", StringComparison.OrdinalIgnoreCase))
                findings.Add(Finding("remote_registry_off", "Remote Registry durdurulmuş", "RemoteRegistry servisi Stopped.", InsightSeverity.Warning, "Security"));

            if (!ctx.AntivirusRunning)
                findings.Add(Finding("av_missing", "Antivirus servisi tespit edilemedi", "WinDefend/MsMpSvc çalışmıyor veya WMI'den okunamadı.", InsightSeverity.Warning, "Security"));

            var criticalEvent = ctx.RecentEventLogs.FirstOrDefault(e =>
                e.Contains("Critical", StringComparison.OrdinalIgnoreCase)
                || e.Contains("Error", StringComparison.OrdinalIgnoreCase));
            if (criticalEvent != null)
                findings.Add(Finding("event_critical", "Son kritik/Error olay kaydı", criticalEvent, InsightSeverity.Critical, "Events"));

            return findings;
        }

        private static AnalysisFinding Finding(string code, string title, string evidence, InsightSeverity severity, string category) =>
            new()
            {
                Code = code,
                Title = title,
                Evidence = evidence,
                Severity = severity,
                Category = category
            };
    }

    public static class ServerRiskEngine
    {
        public static int ComputeRiskScore(ServerAnalysisContext ctx, IReadOnlyList<AnalysisFinding> findings)
        {
            var score = InsightEngine.CalculateRiskScore(
                BuildSnap(ctx),
                ctx.LatencyMs,
                ctx.RdpOpen);

            score -= findings.Count(f => f.Severity == InsightSeverity.Critical) * 5;
            score -= findings.Count(f => f.Severity == InsightSeverity.Warning) * 2;
            return Math.Clamp(score, 0, 100);
        }

        public static List<AnalysisFinding> SelectTopFindings(IReadOnlyList<AnalysisFinding> findings, int max = 5) =>
            findings
                .OrderBy(f => f.Severity switch
                {
                    InsightSeverity.Critical => 0,
                    InsightSeverity.Warning => 1,
                    InsightSeverity.Info => 2,
                    _ => 3
                })
                .ThenBy(f => f.Title)
                .Take(max)
                .ToList();

        private static WmiSnapshot BuildSnap(ServerAnalysisContext ctx) => new()
        {
            Connected = ctx.HasWmiData,
            CpuPct = ctx.CpuPercent,
            RamUsedPct = ctx.RamPercent,
            DiskUsedPct = ctx.DiskPercent,
            Services = ctx.StoppedCriticalServices.Select(s => new ServiceRow(s, "Stopped", "-") { IsCritical = true }).ToList()
        };
    }
}
