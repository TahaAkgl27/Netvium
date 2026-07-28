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
            AddDataQualityFindings(findings, ctx.DataQuality);

            if (!ctx.HasWmiData)
            {
                findings.Add(Finding("wmi_missing", "Canlı sistem verisi alınamadı",
                    "CPU/RAM/Disk ve servis verileri doğrulanamadı; eksik değerler sağlıklı veya sıfır kabul edilmedi.",
                    InsightSeverity.Warning, "Data"));
            }

            AddMetricFinding(findings, "cpu", ctx.CpuTrendAssessment);
            AddMetricFinding(findings, "ram", ctx.RamTrendAssessment);
            AddMetricFinding(findings, "disk", ctx.DiskTrendAssessment);

            if (ctx.LatencyMs is > 50)
                findings.Add(Finding("latency_high", $"Latency {ctx.LatencyMs}ms",
                    $"Ping gecikmesi {ctx.LatencyMs}ms (eşik: >50ms).", InsightSeverity.Warning, "Performance"));

            foreach (var svc in ctx.StoppedCriticalServices.Take(5))
                findings.Add(Finding($"svc_{svc}", $"Kritik servis durdurulmuş: {svc}",
                    $"Servis durumu: Stopped ({svc}).", InsightSeverity.Critical, "Availability"));

            AddAntivirusFindings(findings, ctx);
            AddFirewallFindings(findings, ctx.SystemHealth);

            if (ctx.SystemHealth.PendingRestart == true)
                findings.Add(Finding("pending_restart", "Yeniden başlatma bekleniyor",
                    $"Sinyal: {ctx.SystemHealth.PendingRestartSignal ?? "doğrulanmış probe"}.",
                    InsightSeverity.Warning, "Maintenance"));
            else if (!ctx.SystemHealth.PendingRestart.HasValue)
                findings.Add(Finding("pending_restart_unknown", "Pending restart doğrulanamadı",
                    "Probe sonucu yok; yeniden başlatma gerekli veya gereksiz şeklinde kesin hüküm verilmedi.",
                    InsightSeverity.Info, "Data"));

            AddComponentHealth(findings, "dns", "DNS sağlığı", ctx.SystemHealth.DnsStatus, "Network");
            AddComponentHealth(findings, "time_sync", "Time Sync probe/servis durumu", ctx.SystemHealth.TimeSyncStatus, "Maintenance");
            AddEventFindings(findings, ctx.EventSummary);

            if (ctx.RdpOpen)
                findings.Add(Finding("rdp_open", "RDP portu erişilebilir", "TCP 3389 erişimi doğrulandı.", InsightSeverity.Info, "Security"));
            if (ctx.WinRmRunning)
                findings.Add(Finding("winrm_on", "WinRM aktif", "WinRM servisi Running durumda.", InsightSeverity.Info, "Security"));
            if (string.Equals(ctx.RemoteRegistryState, "Stopped", StringComparison.OrdinalIgnoreCase))
                findings.Add(Finding("remote_registry_off", "Remote Registry durdurulmuş", "RemoteRegistry servisi Stopped.", InsightSeverity.Warning, "Security"));

            return findings;
        }

        private static void AddMetricFinding(List<AnalysisFinding> findings, string code, MetricTrendAssessment metric)
        {
            if (!metric.CurrentValue.HasValue || metric.State is MetricTrendState.Normal or MetricTrendState.Unknown)
                return;

            var severity = metric.State switch
            {
                MetricTrendState.SustainedCritical => InsightSeverity.Critical,
                MetricTrendState.CriticalSpike => InsightSeverity.Critical,
                _ => InsightSeverity.Warning
            };
            var stateText = metric.State switch
            {
                MetricTrendState.SustainedCritical => "sürekli kritik",
                MetricTrendState.SustainedWarning => "sürekli yüksek",
                MetricTrendState.CriticalSpike => "anlık kritik; sustained değil",
                _ => "yüksek; geçmiş yetersiz"
            };
            var findingCode = metric.State == MetricTrendState.CriticalSpike ? $"{code}_spike"
                : metric.State is MetricTrendState.SustainedCritical or MetricTrendState.SustainedWarning ? $"{code}_sustained"
                : $"{code}_history_insufficient";
            findings.Add(Finding(findingCode, $"{metric.Metric} %{metric.CurrentValue:0}: {stateText}",
                metric.Evidence, severity, "Performance"));
        }

        private static void AddAntivirusFindings(List<AnalysisFinding> findings, ServerAnalysisContext ctx)
        {
            var health = ctx.SystemHealth;
            if (health.DefenderEnabled == false && health.ThirdPartyAntivirusHealthy == true)
            {
                findings.Add(Finding("defender_off_other_av", "Defender kapalı; başka antivirus aktif",
                    $"{health.ThirdPartyAntivirusName ?? "Üçüncü taraf antivirus"} sağlıklı görünüyor.",
                    InsightSeverity.Info, "Security"));
                return;
            }

            if (health.DefenderEnabled == false)
            {
                var confirmedMissing = health.ThirdPartyAntivirusHealthy == false;
                var severity = confirmedMissing && ctx.DataQuality.ConfidencePercent >= 60
                    ? InsightSeverity.Critical : InsightSeverity.Warning;
                findings.Add(Finding("av_missing", confirmedMissing ? "Aktif antivirus koruması görülmedi" : "Antivirus koruması doğrulanamadı",
                    confirmedMissing
                        ? $"Defender kapalı ve {health.ThirdPartyAntivirusName ?? "kayıtlı üçüncü taraf antivirus"} sağlıklı değil."
                        : "Defender kapalı; üçüncü taraf antivirus bilgisi mevcut değil.", severity, "Security"));
            }
            else if (!health.DefenderEnabled.HasValue && health.ThirdPartyAntivirusHealthy != true)
            {
                findings.Add(Finding("av_unknown", "Antivirus durumu doğrulanamadı",
                    "Antivirus verisi okunamadı; sistem korumasız kabul edilmedi.", InsightSeverity.Info, "Data"));
            }
        }

        private static void AddFirewallFindings(List<AnalysisFinding> findings, SystemHealthAnalysisData health)
        {
            var profiles = new[]
            {
                (Name: "Domain", Enabled: health.FirewallDomainEnabled),
                (Name: "Private", Enabled: health.FirewallPrivateEnabled),
                (Name: "Public", Enabled: health.FirewallPublicEnabled)
            };
            var known = profiles.Where(p => p.Enabled.HasValue).ToList();
            var disabled = known.Where(p => p.Enabled == false).Select(p => p.Name).ToList();
            if (known.Count == 0)
            {
                findings.Add(Finding("firewall_unknown", "Firewall profilleri doğrulanamadı",
                    "Domain/Private/Public profil verisi okunamadı; kapalı kabul edilmedi.", InsightSeverity.Info, "Data"));
                return;
            }
            if (disabled.Count == 0) return;
            var allKnownDisabled = known.Count == profiles.Length && disabled.Count == profiles.Length;
            findings.Add(Finding(allKnownDisabled ? "firewall_all_off" : "firewall_profile_off",
                allKnownDisabled ? "Tüm firewall profilleri kapalı" : $"Firewall profili kapalı: {string.Join(", ", disabled)}",
                $"Doğrulanan profiller: {string.Join(", ", known.Select(p => $"{p.Name}={(p.Enabled == true ? "On" : "Off")}"))}.",
                allKnownDisabled ? InsightSeverity.Critical : InsightSeverity.Warning, "Security"));
        }

        private static void AddComponentHealth(List<AnalysisFinding> findings, string code, string title, string? status, string category)
        {
            if (string.IsNullOrWhiteSpace(status) || status.Contains("bilin", StringComparison.OrdinalIgnoreCase)
                || status.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(Finding($"{code}_unknown", $"{title} doğrulanamadı",
                    "Probe sonucu yok veya erişilemedi; kesin hata üretilmedi.", InsightSeverity.Info, "Data"));
                return;
            }

            var unhealthy = status.Contains("pasif", StringComparison.OrdinalIgnoreCase)
                || status.Contains("kapalı", StringComparison.OrdinalIgnoreCase)
                || status.Contains("uyarı", StringComparison.OrdinalIgnoreCase)
                || status.Contains("hata", StringComparison.OrdinalIgnoreCase)
                || status.Contains("failed", StringComparison.OrdinalIgnoreCase);
            if (unhealthy)
                findings.Add(Finding($"{code}_unhealthy", $"{title} sorunu", $"Probe durumu: {status}.", InsightSeverity.Warning, category));
        }

        private static void AddEventFindings(List<AnalysisFinding> findings, EventSeveritySummary events)
        {
            if (events.CriticalCount >= 3 || events.ErrorCount >= 5)
                findings.Add(Finding("events_critical_many", "Yakın tarihli kritik olay yoğunluğu",
                    $"Critical={events.CriticalCount}, Error={events.ErrorCount}, Warning={events.WarningCount}.", InsightSeverity.Critical, "Events"));
            else if (events.CriticalCount > 0 || events.ErrorCount > 0)
                findings.Add(Finding("events_errors", "Yakın tarihli kritik/error olayları",
                    $"Critical={events.CriticalCount}, Error={events.ErrorCount}, Warning={events.WarningCount}.", InsightSeverity.Warning, "Events"));
            else if (events.WarningCount >= 5)
                findings.Add(Finding("events_warnings_many", "Yakın tarihli uyarı yoğunluğu",
                    $"Warning={events.WarningCount}.", InsightSeverity.Warning, "Events"));
        }

        private static void AddDataQualityFindings(List<AnalysisFinding> findings, AnalysisDataQuality quality)
        {
            var ageText = quality.Age.HasValue
                ? quality.Age.Value.TotalMinutes >= 1 ? $"{quality.Age.Value.TotalMinutes:0.#} dakika" : $"{quality.Age.Value.TotalSeconds:0} saniye"
                : "bilinmiyor";
            findings.Add(Finding("data_source", $"Veri kaynağı: {quality.Source}",
                $"Veri yaşı: {ageText}; güven: %{quality.ConfidencePercent}; kaynak karşılaştırması: {(quality.SourcesCompared ? "yapıldı" : "yapılmadı")}.",
                quality.IsCache || quality.IsStale ? InsightSeverity.Warning : InsightSeverity.Info, "Data"));
            if (quality.IsCache || quality.IsStale)
                findings.Add(Finding("data_stale", "Snapshot eski veya cache kaynağından",
                    $"Kaynak={quality.Source}; yaş={ageText}. Canlı veri gibi sunulmadı.", InsightSeverity.Warning, "Data"));
            if (quality.HasSourceConflict)
                findings.Add(Finding("source_conflict", "Kaynaklar arasında fark var",
                    $"Çelişen alanlar: {string.Join(", ", quality.Conflicts)}. Confidence düşürüldü.", InsightSeverity.Warning, "Data"));
            if (quality.UnavailableComponents.Count > 0)
                findings.Add(Finding("components_unavailable", "Bazı bileşenler doğrulanamadı",
                    string.Join(", ", quality.UnavailableComponents), InsightSeverity.Info, "Data"));
        }

        private static AnalysisFinding Finding(string code, string title, string evidence, InsightSeverity severity, string category) =>
            new() { Code = code, Title = title, Evidence = evidence, Severity = severity, Category = category };
    }

    public static class ServerRiskEngine
    {
        public static int ComputeRiskScore(ServerAnalysisContext ctx, IReadOnlyList<AnalysisFinding> findings)
        {
            var score = InsightEngine.CalculateRiskScore(BuildSnap(ctx), ctx.LatencyMs, ctx.RdpOpen);
            score -= findings.Count(f => f.Severity == InsightSeverity.Critical) * 5;
            score -= findings.Count(f => f.Severity == InsightSeverity.Warning) * 2;
            return Math.Clamp(score, 0, 100);
        }

        public static List<AnalysisFinding> SelectTopFindings(IReadOnlyList<AnalysisFinding> findings, int max = 5) =>
            findings.OrderBy(f => f.Severity switch
            {
                InsightSeverity.Critical => 0, InsightSeverity.Warning => 1, InsightSeverity.Info => 2, _ => 3
            }).ThenBy(f => f.Title).Take(max).ToList();

        private static WmiSnapshot BuildSnap(ServerAnalysisContext ctx) => new()
        {
            Connected = ctx.HasWmiData,
            CpuPct = ctx.CpuPercent,
            RamUsedPct = ctx.RamPercent,
            DiskUsedPct = ctx.DiskPercent,
            Services = ctx.StoppedCriticalServices.Select(s => new ServiceRow(s, s, "Stopped", true)).ToList()
        };
    }
}