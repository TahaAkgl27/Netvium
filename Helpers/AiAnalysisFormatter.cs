using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class AiAnalysisFormatter
    {
        public const string SectionServerRole = "SERVER ROLE";
        public const string SectionHealthScore = "HEALTH SCORE";
        public const string SectionTopRisks = "TOP RISKS";
        public const string SectionCriticalFindings = "TOP RISKS";
        public const string SectionPerformance = "PERFORMANCE";
        public const string SectionSecurity = "SECURITY";
        public const string SectionEventAnalysis = "EVENT ANALYSIS";
        public const string SectionPrediction = "PREDICTION";
        public const string SectionRecommendedActions = "RECOMMENDED ACTIONS";
        public const string SectionEvidence = "EVIDENCE";

        public static AiSmartAnalysisReport BuildRuleBasedReport(ServerAnalysisContext ctx)
        {
            var report = new AiSmartAnalysisReport { FromAi = false };

            report.ServerRole = new AiServerRoleSection
            {
                Role = ctx.DetectedRole,
                Confidence = ctx.RoleConfidence,
                Evidence = ctx.RoleEvidence.Count > 0 ? ctx.RoleEvidence : new List<string> { "Yeterli rol kanıtı bulunamadı" }
            };

            report.HealthScore = new AiFindingItem
            {
                Text = $"Bağlantı sağlığı {ctx.HealthScore}/100, risk skoru {ctx.RiskScore}/100.",
                Evidence = ctx.TopFindings.Count > 0
                    ? string.Join("; ", ctx.TopFindings.Take(2).Select(f => f.Evidence))
                    : "Kural motoru bulgusu yok"
            };

            report.CriticalFindings = ctx.TopFindings
                .Take(5)
                .Select(f => new AiFindingItem { Text = f.Title, Evidence = f.Evidence })
                .ToList();

            report.Performance = BuildPerformance(ctx);
            report.Security = BuildSecurity(ctx);
            report.EventAnalysis = BuildEventAnalysis(ctx);
            report.Prediction = ctx.DiskTrendPrediction ?? new AiPredictionSection
            {
                Available = false,
                Text = "Tahmin üretilemedi.",
                Evidence = "Trend verisi veya yeterli zaman serisi yok."
            };
            report.RecommendedActions = BuildActionsFromFindings(ctx.RuleFindings);
            report.Evidence = BuildEvidence(ctx);
            return report;
        }

        public static List<SmartInsight> ToSmartInsights(AiSmartAnalysisReport report)
        {
            var list = new List<SmartInsight>();
            var sort = 0;

            if (report.ServerRole != null && !string.IsNullOrWhiteSpace(report.ServerRole.Role))
            {
                AddSectionHeader(list, ref sort, SectionServerRole);
                AddItem(list, ref sort, SectionServerRole, report.ServerRole.Role, report.FromAi,
                    $"Confidence {report.ServerRole.Confidence}%",
                    report.ServerRole.Evidence.Count > 0 ? $"Kanıt: {string.Join(", ", report.ServerRole.Evidence)}" : null,
                    InsightSeverity.Info);
            }

            if (report.HealthScore != null && !string.IsNullOrWhiteSpace(report.HealthScore.Text))
            {
                AddSectionHeader(list, ref sort, SectionHealthScore);
                AddItem(list, ref sort, SectionHealthScore, report.HealthScore.Text, report.FromAi,
                    report.HealthScore.Evidence, null, InsightSeverity.Info);
            }

            if (report.CriticalFindings.Count > 0)
            {
                AddSectionHeader(list, ref sort, SectionTopRisks);
                foreach (var item in report.CriticalFindings.Take(5))
                    AddItem(list, ref sort, SectionTopRisks, $"• {item.Text}", report.FromAi, item.Evidence, null,
                        InsightSeverity.Critical);
            }

            if (report.Performance.Count > 0)
            {
                AddSectionHeader(list, ref sort, SectionPerformance);
                foreach (var item in report.Performance)
                    AddItem(list, ref sort, SectionPerformance, item.Text, report.FromAi, item.Evidence, null, InsightSeverity.Info);
            }

            if (report.Security.Count > 0)
            {
                AddSectionHeader(list, ref sort, SectionSecurity);
                foreach (var item in report.Security)
                {
                    var sev = item.RiskLevel?.Equals("Critical", StringComparison.OrdinalIgnoreCase) == true
                        ? InsightSeverity.Critical
                        : item.RiskLevel?.Equals("Warning", StringComparison.OrdinalIgnoreCase) == true
                            ? InsightSeverity.Warning
                            : InsightSeverity.Info;
                    AddItem(list, ref sort, SectionSecurity, item.Text, report.FromAi, item.Evidence, item.RiskLevel, sev);
                }
            }

            if (report.EventAnalysis.Count > 0)
            {
                AddSectionHeader(list, ref sort, SectionEventAnalysis);
                foreach (var item in report.EventAnalysis.Take(4))
                    AddItem(list, ref sort, SectionEventAnalysis, item.Text, report.FromAi, item.Evidence, null, InsightSeverity.Warning);
            }

            if (report.Prediction != null && report.Prediction.Available && !string.IsNullOrWhiteSpace(report.Prediction.Text))
            {
                AddSectionHeader(list, ref sort, SectionPrediction);
                AddItem(list, ref sort, SectionPrediction, report.Prediction.Text, report.FromAi, report.Prediction.Evidence, null, InsightSeverity.Tip);
            }

            if (report.RecommendedActions.Count > 0)
            {
                AddSectionHeader(list, ref sort, SectionRecommendedActions);
                foreach (var action in report.RecommendedActions)
                {
                    var sev = action.Priority.Equals("HIGH", StringComparison.OrdinalIgnoreCase) ? InsightSeverity.Critical
                        : action.Priority.Equals("LOW", StringComparison.OrdinalIgnoreCase) ? InsightSeverity.Tip
                        : InsightSeverity.Warning;
                    AddItem(list, ref sort, SectionRecommendedActions, $"{action.Priority}: {action.Text}", report.FromAi, action.Evidence, action.Priority, sev);
                }
            }

            if (report.Evidence.Count > 0 && !report.FromAi)
            {
                AddSectionHeader(list, ref sort, SectionEvidence);
                foreach (var ev in report.Evidence.Take(6))
                    AddItem(list, ref sort, SectionEvidence, $"{ev.Metric}: {ev.Value}", report.FromAi, null, null, InsightSeverity.Info);
            }

            return list;
        }

        public static bool IsQualityReport(IReadOnlyList<SmartInsight> insights) =>
            insights.Count(i => !i.IsSectionHeader) >= 4;

        private static List<AiFindingItem> BuildPerformance(ServerAnalysisContext ctx)
        {
            var items = new List<AiFindingItem>();
            if (ctx.CpuPercent.HasValue)
                items.Add(Item(CpuLabel(ctx.CpuPercent.Value), $"CPU={ctx.CpuPercent}%"));
            if (ctx.RamPercent.HasValue)
                items.Add(Item(RamLabel(ctx.RamPercent.Value), $"RAM={ctx.RamPercent:0}%"));
            if (ctx.DiskPercent.HasValue)
                items.Add(Item(DiskLabel(ctx.DiskPercent.Value), $"Disk={ctx.DiskPercent:0}%"));
            if (ctx.LatencyMs.HasValue)
                items.Add(Item(LatencyLabel(ctx.LatencyMs.Value), $"Latency={ctx.LatencyMs}ms"));
            return items;
        }

        private static List<AiFindingItem> BuildSecurity(ServerAnalysisContext ctx)
        {
            var items = new List<AiFindingItem>();
            items.Add(Item($"RDP: {(ctx.RdpOpen ? "erişilebilir" : "kapalı")}", ctx.RdpOpen ? "TCP 3389 açık" : "3389 kapalı", ctx.RdpOpen ? "Info" : "Warning"));
            items.Add(Item($"WinRM: {(ctx.WinRmRunning ? "aktif" : "pasif")}", ctx.WinRmRunning ? "WinRM Running" : "WinRM stopped/unknown", ctx.WinRmRunning ? "Info" : "Warning"));
            items.Add(Item($"Firewall: {(ctx.FirewallRunning ? "aktif" : "devre dışı")}", ctx.FirewallRunning ? "mpssvc Running" : "mpssvc stopped", ctx.FirewallRunning ? "Info" : "Critical"));
            items.Add(Item($"Remote Registry: {ctx.RemoteRegistryState}", $"Servis durumu: {ctx.RemoteRegistryState}", "Info"));
            items.Add(Item($"SMB: {(ctx.SmbRunning ? "aktif" : "pasif")}", "LanmanServer durumu", "Info"));
            if (!string.IsNullOrWhiteSpace(ctx.LastUser))
                items.Add(Item($"Son kullanıcı: {ctx.LastUser}", "WMI LastUser", "Info"));
            if (ctx.OpenPorts.Count > 0)
                items.Add(Item($"Açık portlar: {string.Join(", ", ctx.OpenPorts.Take(8))}", "ListenPorts WMI", "Info"));
            return items;
        }

        private static List<AiFindingItem> BuildEventAnalysis(ServerAnalysisContext ctx)
        {
            var items = new List<AiFindingItem>();
            if (ctx.RecentEventLogs.Count == 0)
            {
                items.Add(Item("Son dönemde önemli olay kaydı yok.", "Event log verisi alınamadı veya kayıt yok."));
                return items;
            }

            foreach (var ev in ctx.RecentEventLogs.Take(3))
                items.Add(Item(ev, "WMI Event Log"));
            return items;
        }

        private static List<AiActionItem> BuildActionsFromFindings(IReadOnlyList<AnalysisFinding> findings)
        {
            return findings
                .OrderBy(f => f.Severity)
                .Take(8)
                .Select(f => new AiActionItem
                {
                    Priority = f.Severity == InsightSeverity.Critical ? "HIGH"
                        : f.Severity == InsightSeverity.Warning ? "MEDIUM" : "LOW",
                    Text = f.Title,
                    Evidence = f.Evidence
                })
                .ToList();
        }

        private static List<AiEvidenceItem> BuildEvidence(ServerAnalysisContext ctx)
        {
            var list = new List<AiEvidenceItem>();
            if (ctx.CpuPercent.HasValue) list.Add(Ev("CPU", $"{ctx.CpuPercent}%"));
            if (ctx.RamPercent.HasValue) list.Add(Ev("RAM", $"{ctx.RamPercent:0}%"));
            if (ctx.DiskPercent.HasValue) list.Add(Ev("Disk", $"{ctx.DiskPercent:0}%"));
            if (ctx.LatencyMs.HasValue) list.Add(Ev("Latency", $"{ctx.LatencyMs}ms"));
            list.Add(Ev("Health Score", $"{ctx.HealthScore}"));
            list.Add(Ev("Risk Score", $"{ctx.RiskScore}"));
            if (ctx.StoppedCriticalServices.Count > 0)
                list.Add(Ev("Stopped Services", string.Join(", ", ctx.StoppedCriticalServices.Take(4))));
            if (ctx.RecentEventLogs.Count > 0)
                list.Add(Ev("Recent Event", ctx.RecentEventLogs[0]));
            return list;
        }

        private static void AddSectionHeader(List<SmartInsight> list, ref int sort, string section)
        {
            list.Add(new SmartInsight(section, InsightSeverity.Info, "📋", isAi: true)
            {
                IsSectionHeader = true,
                Section = section,
                SortKey = sort++
            });
        }

        private static void AddItem(List<SmartInsight> list, ref int sort, string section, string text, bool isAi, string? reason, string? action, InsightSeverity severity)
        {
            list.Add(new SmartInsight(text, severity, isAi ? "🤖" : "⚙️", isAi: isAi, reason: reason, action: action)
            {
                Section = section,
                SortKey = sort++,
                Priority = action
            });
        }

        private static AiFindingItem Item(string text, string evidence, string? risk = null) =>
            new() { Text = text, Evidence = evidence, RiskLevel = risk };

        private static AiEvidenceItem Ev(string metric, string value) => new() { Metric = metric, Value = value };

        private static string CpuLabel(int v) => v > 85 ? "CPU yüksek seviyede." : v > 60 ? "CPU orta seviyede." : "CPU normal seviyede.";
        private static string RamLabel(double v) => v > 90 ? "RAM kullanımı kritik." : v > 75 ? "RAM kullanımı yükseliyor." : "RAM kullanımı düşük.";
        private static string DiskLabel(double v) => v > 80 ? "Disk kullanımı yüksek." : v > 60 ? "Disk kullanımı orta." : "Disk kullanımı düşük.";
        private static string LatencyLabel(long ms) => ms > 150 ? "Latency yüksek." : ms > 50 ? "Latency orta." : "Latency mükemmel.";
    }
}
