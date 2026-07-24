using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    public static class InsightEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private static readonly ConcurrentDictionary<string, long> _aiCooldowns = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _aiInFlight = new();
        private static readonly ConcurrentDictionary<string, string> _aiStatusMessages = new();
        private static AppSettings _settings = new AppSettings();

        public static void Configure(AppSettings settings) => _settings = settings ?? new AppSettings();

        public static void ClearAiCooldown(string serverKey)
        {
            _aiCooldowns.TryRemove(serverKey, out _);
            _aiStatusMessages.TryRemove(serverKey, out _);
        }

        public static TimeSpan GetAiCooldownRemaining(string serverKey, int cooldownSeconds = 60)
        {
            if (!_settings.AiCacheEnabled || !_aiCooldowns.TryGetValue(serverKey, out var started))
                return TimeSpan.Zero;
            var elapsedTicks = Stopwatch.GetTimestamp() - started;
            var elapsed = elapsedTicks <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(elapsedTicks / (double)Stopwatch.Frequency);
            var remaining = TimeSpan.FromSeconds(Math.Max(1, cooldownSeconds)) - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        public static bool IsAiRequestInFlight(string serverKey) =>
            _aiInFlight.TryGetValue(serverKey, out var gate) && gate.CurrentCount == 0;

        public static string? GetLastAiStatusMessage(string serverKey) =>
            _aiStatusMessages.TryGetValue(serverKey, out var message) ? message : null;

        public static bool IsAiCooldownElapsed(string serverKey, int cooldownSeconds = 60) =>
            GetAiCooldownRemaining(serverKey, cooldownSeconds) <= TimeSpan.Zero;

        public static void ClearAllAiCooldowns()
        {
            _aiCooldowns.Clear();
            _aiStatusMessages.Clear();
        }

        public static bool HasSufficientLiveDataForAi(WmiSnapshot? current, bool rdpOpen, long? pingMs)
        {
            if (current == null)
                return false;

            var hasWmi = current.Connected
                && (current.CpuPct.HasValue
                    || current.RamUsedPct.HasValue
                    || current.DiskUsedPct.HasValue
                    || !string.IsNullOrWhiteSpace(current.OSName)
                    || current.Services?.Count > 0);

            return hasWmi || rdpOpen || pingMs is >= 0;
        }

        public static bool IsQualityAiInsight(SmartInsight insight)
        {
            if (string.IsNullOrWhiteSpace(insight?.Text))
                return false;
            var text = insight.Text.Trim();
            if (text.Length < 12)
                return false;
            var generic = new[] { "database server", "web server", "file server", "normal", "ok", "healthy" };
            if (generic.Any(g => text.Equals(g, StringComparison.OrdinalIgnoreCase)))
                return false;
            return !string.IsNullOrWhiteSpace(insight.Reason) || !string.IsNullOrWhiteSpace(insight.Action);
        }

        public static List<SmartInsight> MergeAiInsights(IReadOnlyList<SmartInsight> existing, IReadOnlyList<SmartInsight> incoming)
        {
            if (incoming == null || incoming.Count == 0)
                return existing?.ToList() ?? new List<SmartInsight>();

            var qualityIncoming = incoming.Where(x => x.IsAi && IsQualityAiInsight(x)).ToList();
            if (qualityIncoming.Count == 0)
                return existing?.ToList() ?? new List<SmartInsight>();

            var merged = existing?.Where(x => !x.IsAi).ToList() ?? new List<SmartInsight>();
            var keptAi = existing?.Where(x => x.IsAi && IsQualityAiInsight(x)).ToList() ?? new List<SmartInsight>();

            foreach (var ins in qualityIncoming)
            {
                if (!merged.Any(x => x.IsAi && x.Text == ins.Text))
                    merged.Add(ins);
            }

            if (keptAi.Count > qualityIncoming.Count)
            {
                foreach (var old in keptAi)
                {
                    if (!merged.Any(x => x.IsAi && x.Text == old.Text))
                        merged.Add(old);
                }
            }

            return merged;
        }

        public static List<SmartInsight> Analyze(WmiSnapshot current, List<WmiSnapshot>? history, long? pingMs, bool rdpOpen)
        {
            var insights = new List<SmartInsight>();
            if (current == null) return insights;

            if (!rdpOpen)
                insights.Add(new SmartInsight("Bağlantı Riski: RDP Portu Kapalı", InsightSeverity.Critical, "🚫",
                    reason: "3389 numaralı RDP portuna ulaşılamıyor.",
                    action: "Güvenlik duvarı ve RDP servis durumunu kontrol edin."));
            else
                insights.Add(new SmartInsight("RDP Portu Erişilebilir", InsightSeverity.Info, "🟢",
                    reason: "Uzak masaüstü bağlantı katmanı aktif.",
                    action: "Bağlantı kurulabilir."));

            if (pingMs is >= 0)
            {
                var latencyColor = GetLatencyClass(pingMs.Value);
                insights.Add(new SmartInsight($"Gecikme: {pingMs.Value}ms ({latencyColor.label})", latencyColor.severity, "📡",
                    reason: pingMs >= 150 ? "Yüksek gecikme kullanıcı deneyimini etkileyebilir." : "Ağ gecikmesi kabul edilebilir düzeyde.",
                    action: pingMs >= 150 ? "Ağ yolunu ve yoğunluğu kontrol edin." : null));
            }

            if (!current.Connected)
            {
                insights.Add(new SmartInsight("WMI verisi alınamadı", InsightSeverity.Warning, "⚠️",
                    reason: "Yalnızca ağ erişimi doğrulandı; CPU/RAM/Disk ve servis listesi canlı alınamadı.",
                    action: "Kimlik bilgilerini ve WMI/RPC erişimini kontrol edip Yenile ile tekrar deneyin."));
                return FilterInsights(insights);
            }

            if (current.DiskUsedPct is >= 80)
            {
                var sev = current.DiskUsedPct >= 90 ? InsightSeverity.Critical : InsightSeverity.Warning;
                insights.Add(new SmartInsight($"Disk Kullanımı: %{current.DiskUsedPct:0}", sev, "💾",
                    reason: current.DiskUsedPct >= 90 ? "Disk doluluğu kritik seviyede." : "Disk kullanımı yükseliyor.",
                    action: "Gereksiz dosyaları temizleyin veya diski genişletin."));
            }

            if (current.RamUsedPct is >= 85)
                insights.Add(new SmartInsight($"RAM Kullanımı: %{current.RamUsedPct:0}", InsightSeverity.Warning, "🧠",
                    reason: "Bellek baskısı performansı düşürebilir.",
                    action: "Bellek tüketen işlemleri ve servisleri gözden geçirin."));

            if (current.CpuPct is >= 80)
            {
                var sev = current.CpuPct >= 95 ? InsightSeverity.Critical : InsightSeverity.Warning;
                insights.Add(new SmartInsight($"CPU Kullanımı: %{current.CpuPct}", sev, "🔥",
                    reason: "İşlemci yükü yüksek.",
                    action: "CPU tüketen süreçleri ve zamanlanmış görevleri kontrol edin."));
            }

            if (current.Uptime.HasValue)
            {
                double days = current.Uptime.Value.TotalDays;
                if (days > 90)
                    insights.Add(new SmartInsight($"Uptime: {days:0} gün", InsightSeverity.Warning, "📅",
                        reason: "Uzun süreli çalışma yama/stabilite birikimine işaret edebilir.",
                        action: "Bakım penceresinde yeniden başlatmayı planlayın."));
            }

            if (current.Services != null && current.Services.Any())
            {
                var criticalStopped = current.Services
                    .Where(s => s.IsCritical && !s.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (criticalStopped.Any())
                {
                    insights.Add(new SmartInsight($"{criticalStopped.Count} kritik servis durdurulmuş", InsightSeverity.Critical, "🚨",
                        reason: $"Servisler: {string.Join(", ", criticalStopped.Take(4).Select(s => s.Display))}",
                        action: "Kritik servisleri başlatın ve bağımlılıklarını kontrol edin."));
                }

                var iis = current.Services.Any(s => s.Name.Contains("W3SVC", StringComparison.OrdinalIgnoreCase) || s.Display.Contains("IIS", StringComparison.OrdinalIgnoreCase));
                var sql = current.Services.Any(s => s.Name.Contains("MSSQL", StringComparison.OrdinalIgnoreCase) || s.Display.Contains("SQL", StringComparison.OrdinalIgnoreCase));
                if (iis)
                    insights.Add(new SmartInsight("IIS rolü tespit edildi", InsightSeverity.Info, "🌐",
                        reason: "W3SVC/IIS ile ilişkili servis kaydı mevcut.", action: "Uygulama havuzu ve site sağlığını izleyin."));
                if (sql)
                    insights.Add(new SmartInsight("SQL Server bileşeni tespit edildi", InsightSeverity.Info, "🗄️",
                        reason: "SQL ile ilişkili servis kaydı mevcut.", action: "SQL agent ve disk I/O metriklerini izleyin."));
            }

            if (current.Timeline?.Count > 0)
            {
                var recent = current.Timeline.Take(3).Select(t => $"{t.Type}: {t.Message}").ToList();
                insights.Add(new SmartInsight("Son olay kayıtları özeti", InsightSeverity.Info, "📋",
                    reason: string.Join(" | ", recent),
                    action: "Kritik Error/Warning kayıtlarını inceleyin."));
            }

            return FilterInsights(insights);
        }

        public static SmartInsight BuildInsufficientAiInsight() => new SmartInsight(
            "AI analizi için yeterli canlı veri yok. Yenile ile tekrar deneyin.",
            InsightSeverity.Info,
            "ℹ️",
            isAi: true,
            reason: "Canlı WMI metrikleri veya servis listesi henüz alınamadı.",
            action: "Sunucuyu seçip Yenile çalıştırın; ardından AI analizini tekrarlayın.");

        private static List<SmartInsight> FilterInsights(List<SmartInsight> insights)
        {
            if (_settings.GenerateMaintenanceSuggestions && _settings.GeneratePerformanceSuggestions)
                return insights;

            var filtered = new List<SmartInsight>();
            foreach (var ins in insights)
            {
                var text = (ins.Text + ins.Reason).ToLowerInvariant();
                var isMaintenance = text.Contains("reboot") || text.Contains("restart") || text.Contains("yama") ||
                                    text.Contains("patch") || text.Contains("uptime") || text.Contains("güncelle");
                var isPerformance = text.Contains("cpu") || text.Contains("ram") || text.Contains("disk") ||
                                    text.Contains("bellek") || text.Contains("işlemci");

                if (isMaintenance && !_settings.GenerateMaintenanceSuggestions)
                    continue;
                if (isPerformance && !_settings.GeneratePerformanceSuggestions)
                    continue;

                filtered.Add(ins);
            }

            return filtered;
        }

        private static (string label, InsightSeverity severity) GetLatencyClass(long ms)
        {
            if (ms < 10) return ("Ultra Hızlı", InsightSeverity.Tip);
            if (ms < 50) return ("Stabil / İyi", InsightSeverity.Tip);
            if (ms < 150) return ("Orta / Gecikmeli", InsightSeverity.Info);
            if (ms < 300) return ("Yüksek Gecikme", InsightSeverity.Warning);
            return ("Kritik Seviye / Lag", InsightSeverity.Critical);
        }

        public static int CalculateRiskScore(WmiSnapshot? snap, long? pingMs, bool rdpOpen)
        {
            double score = 100;

            if (!rdpOpen) return 0;

            if (pingMs.HasValue)
            {
                if (pingMs > 500) score -= 40;
                else if (pingMs > 250) score -= 20;
                else if (pingMs > 150) score -= 10;
            }
            else score -= 30;

            if (snap != null && snap.Connected)
            {
                if (snap.CpuPct > 95) score -= 30;
                if (snap.RamUsedPct > 95) score -= 25;
                if (snap.DiskUsedPct > 95) score -= 40;
                if (snap.Uptime?.TotalDays > 180) score -= 15;
                if (snap.Services?.Count(s => s.IsCritical && s.State != "Running") > 0) score -= 20;
            }
            else score -= 20;

            return (int)Math.Max(0, score);
        }

        public static List<SmartInsight> BuildRuleOnlyInsights(ServerAnalysisContext ctx)
        {
            var report = AiAnalysisFormatter.BuildRuleBasedReport(ctx);
            return AiAnalysisFormatter.ToSmartInsights(report);
        }

        public static async Task<AiSmartAnalysisReport?> GetAiReportAsync(
            string apiKey,
            string serverKey,
            ServerAnalysisContext ctx,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureEntitlementService.Instance.IsAiAnalysisEnabled(_settings)
                || !_settings.EnableAiAnalysis || !_settings.AutomaticRecommendations || string.IsNullOrWhiteSpace(apiKey))
                return null;
            if (!ctx.HasWmiData && !ctx.RdpOpen && (ctx.LatencyMs is null or < 0))
                return null;

            var gate = _aiInFlight.GetOrAdd(serverKey, _ => new SemaphoreSlim(1, 1));
            bool acquired;
            try
            {
                acquired = await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            if (!acquired)
                return null;

            try
            {
                if (!IsAiCooldownElapsed(serverKey))
                    return null;

                _aiCooldowns[serverKey] = Stopwatch.GetTimestamp();
                _aiStatusMessages.TryRemove(serverKey, out _);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.AiTimeoutSeconds, 5, 120)));
                var token = timeoutCts.Token;

                var prompt = BuildAiV2Prompt(ctx);
                var requestBody = new
                {
                    model = string.IsNullOrWhiteSpace(_settings.AiModel) ? "gpt-4o-mini" : _settings.AiModel,
                    messages = new[] { new { role = "user", content = prompt } },
                    max_tokens = _settings.AnalysisDepth switch
                    {
                        "quick" => Math.Min(700, _settings.AiTokenLimit),
                        "deep" => Math.Min(1200, _settings.AiTokenLimit),
                        _ => Math.Min(_settings.AiTokenLimit, 900)
                    },
                    temperature = 0.2,
                    response_format = new { type = "json_object" }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, GetAiEndpoint());
                if (_settings.AiProvider == "Azure OpenAI")
                    request.Headers.Add("api-key", apiKey);
                else
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                var jsonStr = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _aiStatusMessages[serverKey] = "AI analizi tamamlanamadı. Lütfen tekrar deneyin.";
                    return null;
                }

                using var doc = JsonDocument.Parse(jsonStr);
                var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                var report = string.IsNullOrWhiteSpace(content) ? null : ParseAiV2Response(content);
                if (report == null)
                {
                    _aiStatusMessages[serverKey] = "AI yanıtı işlenemedi. Lütfen tekrar deneyin.";
                    return null;
                }

                report.FromAi = true;
                RefreshDebugLog.AiQuality(serverKey, CountAiSections(ctx), CountReportSections(report));
                return report;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User/view shutdown cancellation is expected and is not an error.
                return null;
            }
            catch (OperationCanceledException)
            {
                _aiStatusMessages[serverKey] = "AI analizi zaman aşımına uğradı. Lütfen tekrar deneyin.";
                return null;
            }
            catch (Exception ex)
            {
                _aiStatusMessages[serverKey] = "AI analizi tamamlanamadı. Lütfen tekrar deneyin.";
                AppLogger.Warning($"AI V2 analysis failed: {ex.GetType().Name}", category: "AI", action: "Analysis");
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        public static List<SmartInsight> MergeAiWithRuleReport(AiSmartAnalysisReport? aiReport, ServerAnalysisContext ctx)
        {
            var ruleReport = AiAnalysisFormatter.BuildRuleBasedReport(ctx);
            if (aiReport == null || !IsQualityAiReport(aiReport))
                return AiAnalysisFormatter.ToSmartInsights(ruleReport);

            EnrichAiReportFromRules(aiReport, ctx, ruleReport);
            return AiAnalysisFormatter.ToSmartInsights(aiReport);
        }

        public static bool IsQualityAiReport(AiSmartAnalysisReport report)
        {
            if (report == null || !report.FromAi)
                return false;
            var sections = CountReportSections(report);
            if (sections < 3)
                return false;
            var text = string.Join(" ",
                report.CriticalFindings.Select(f => f.Text)
                    .Concat(report.Performance.Select(f => f.Text))
                    .Concat(report.Security.Select(f => f.Text)));
            if (ContainsVagueLanguage(text))
                return false;
            return report.CriticalFindings.All(f => !string.IsNullOrWhiteSpace(f.Evidence))
                   || report.Performance.Count > 0;
        }

        private static void EnrichAiReportFromRules(AiSmartAnalysisReport ai, ServerAnalysisContext ctx, AiSmartAnalysisReport rule)
        {
            if (ai.ServerRole == null || string.IsNullOrWhiteSpace(ai.ServerRole.Role))
                ai.ServerRole = rule.ServerRole;
            if (ai.HealthScore == null || string.IsNullOrWhiteSpace(ai.HealthScore.Text))
                ai.HealthScore = rule.HealthScore;
            if (ai.CriticalFindings.Count == 0)
                ai.CriticalFindings = rule.CriticalFindings;
            if (ai.EventAnalysis.Count == 0)
                ai.EventAnalysis = rule.EventAnalysis;
            if (ai.Evidence.Count == 0)
                ai.Evidence = rule.Evidence;
            if (ai.RecommendedActions.Count == 0)
                ai.RecommendedActions = rule.RecommendedActions;
            if (ai.Prediction == null || !ai.Prediction.Available)
                ai.Prediction = rule.Prediction;
        }

        private static int CountAiSections(ServerAnalysisContext ctx)
        {
            var n = 0;
            if (!string.IsNullOrWhiteSpace(ctx.OsName)) n++;
            if (ctx.CpuPercent.HasValue || ctx.RamPercent.HasValue || ctx.DiskPercent.HasValue) n++;
            if (ctx.RuleFindings.Count > 0) n++;
            if (ctx.RecentEventLogs.Count > 0) n++;
            if (ctx.RunningServices.Count > 0) n++;
            return n;
        }

        private static int CountReportSections(AiSmartAnalysisReport r)
        {
            var n = 0;
            if (r.ServerRole != null) n++;
            if (r.HealthScore != null) n++;
            if (r.CriticalFindings.Count > 0) n++;
            if (r.Performance.Count > 0) n++;
            if (r.Security.Count > 0) n++;
            if (r.EventAnalysis.Count > 0) n++;
            if (r.Prediction != null) n++;
            if (r.RecommendedActions.Count > 0) n++;
            return n;
        }

        private static bool ContainsVagueLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var lower = text.ToLowerInvariant();
            string[] banned = { "it appears", "maybe", "perhaps", "might be", "could be", "gibi görünüyor", "belki" };
            return banned.Any(b => lower.Contains(b));
        }

        private static string BuildAiV2Prompt(ServerAnalysisContext ctx)
        {
            var payload = ServerAnalysisPipeline.BuildAiPayloadJson(ctx);
            return $@"Sen deneyimli bir Windows Server yöneticisisin. Yalnızca verilen teknik veriyi yorumla.
ASLA kanıtsız bilgi üretme. Emin değilsen ""Muhtemelen"" veya ""Kesin değil"" kullan.
Bilgi yoksa ""Yeterli veri bulunamadı."" de. Her bölüm en fazla 2-3 cümle olsun.

{payload}

JSON çıktı (Türkçe, somut, kanıtlı):
{{
  ""server_role"": {{ ""role"": ""..."", ""confidence"": 92, ""evidence"": [""...""] }},
  ""health_score"": {{ ""text"": ""..."", ""evidence"": ""..."" }},
  ""top_risks"": [{{ ""text"": ""..."", ""evidence"": ""..."" }}],
  ""performance"": [{{ ""text"": ""..."", ""evidence"": ""..."" }}],
  ""security"": [{{ ""text"": ""..."", ""evidence"": ""..."", ""risk_level"": ""Critical|Warning|Info"" }}],
  ""event_analysis"": [{{ ""text"": ""..."", ""evidence"": ""..."" }}],
  ""prediction"": {{ ""available"": true/false, ""text"": ""..."", ""evidence"": ""..."" }},
  ""recommended_actions"": [{{ ""priority"": ""HIGH|MEDIUM|LOW"", ""text"": ""..."", ""evidence"": ""..."" }}]
}}

Kurallar:
- top_risks en fazla 3 madde, rule engine bulgularını önceliklendir.
- event_analysis yalnızca event log verisi varsa doldur.
- prediction yalnızca trend kanıtı varsa available=true.
- Her maddede evidence zorunlu.";
        }

        private static AiSmartAnalysisReport? ParseAiV2Response(string content)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var report = new AiSmartAnalysisReport { FromAi = true };

                if (root.TryGetProperty("server_role", out var roleEl))
                {
                    report.ServerRole = new AiServerRoleSection
                    {
                        Role = roleEl.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "",
                        Confidence = roleEl.TryGetProperty("confidence", out var c) && c.TryGetInt32(out var ci) ? ci : 0,
                        Evidence = ReadStringArray(roleEl, "evidence")
                    };
                }

                if (root.TryGetProperty("health_score", out var healthEl))
                {
                    report.HealthScore = new AiFindingItem
                    {
                        Text = healthEl.TryGetProperty("text", out var ht) ? ht.GetString() ?? "" : "",
                        Evidence = healthEl.TryGetProperty("evidence", out var he) ? he.GetString() ?? "" : ""
                    };
                }

                report.CriticalFindings = ReadFindingArray(root, "top_risks");
                if (report.CriticalFindings.Count == 0)
                    report.CriticalFindings = ReadFindingArray(root, "critical_findings");

                report.Performance = ReadFindingArray(root, "performance");
                report.Security = ReadSecurityArray(root, "security");
                report.EventAnalysis = ReadFindingArray(root, "event_analysis");

                if (root.TryGetProperty("prediction", out var predEl))
                {
                    report.Prediction = new AiPredictionSection
                    {
                        Available = predEl.TryGetProperty("available", out var av) && av.GetBoolean(),
                        Text = predEl.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                        Evidence = predEl.TryGetProperty("evidence", out var e) ? e.GetString() ?? "" : ""
                    };
                }

                report.RecommendedActions = ReadActionArray(root, "recommended_actions");
                report.Evidence = ReadEvidenceArray(root, "evidence");
                return report;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ReadStringArray(JsonElement parent, string name)
        {
            var list = new List<string>();
            if (!parent.TryGetProperty(name, out var arr)) return list;
            foreach (var item in arr.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
            return list;
        }

        private static List<AiFindingItem> ReadFindingArray(JsonElement root, string name)
        {
            var list = new List<AiFindingItem>();
            if (!root.TryGetProperty(name, out var arr)) return list;
            foreach (var item in arr.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new AiFindingItem
                {
                    Text = text,
                    Evidence = item.TryGetProperty("evidence", out var e) ? e.GetString() ?? "" : ""
                });
            }
            return list;
        }

        private static List<AiFindingItem> ReadSecurityArray(JsonElement root, string name)
        {
            var list = new List<AiFindingItem>();
            if (!root.TryGetProperty(name, out var arr)) return list;
            foreach (var item in arr.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new AiFindingItem
                {
                    Text = text,
                    Evidence = item.TryGetProperty("evidence", out var e) ? e.GetString() ?? "" : "",
                    RiskLevel = item.TryGetProperty("risk_level", out var r) ? r.GetString() : null
                });
            }
            return list;
        }

        private static List<AiActionItem> ReadActionArray(JsonElement root, string name)
        {
            var list = new List<AiActionItem>();
            if (!root.TryGetProperty(name, out var arr)) return list;
            foreach (var item in arr.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new AiActionItem
                {
                    Priority = item.TryGetProperty("priority", out var p) ? p.GetString() ?? "MEDIUM" : "MEDIUM",
                    Text = text,
                    Evidence = item.TryGetProperty("evidence", out var e) ? e.GetString() ?? "" : ""
                });
            }
            return list;
        }

        private static List<AiEvidenceItem> ReadEvidenceArray(JsonElement root, string name)
        {
            var list = new List<AiEvidenceItem>();
            if (!root.TryGetProperty(name, out var arr)) return list;
            foreach (var item in arr.EnumerateArray())
            {
                list.Add(new AiEvidenceItem
                {
                    Metric = item.TryGetProperty("metric", out var m) ? m.GetString() ?? "" : "",
                    Value = item.TryGetProperty("value", out var v) ? v.GetString() ?? "" : ""
                });
            }
            return list;
        }

        private static string GetAiEndpoint()
        {
            if (_settings.AiProvider == "Azure OpenAI")
            {
                var baseUrl = Environment.GetEnvironmentVariable("NETVIUM_AZURE_OPENAI_ENDPOINT");
                var deployment = string.IsNullOrWhiteSpace(_settings.AiModel) ? "gpt-4o-mini" : _settings.AiModel;
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    return $"{baseUrl.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version=2024-08-01-preview";
            }

            return "https://api.openai.com/v1/chat/completions";
        }
    }
}
