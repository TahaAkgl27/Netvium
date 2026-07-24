using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    public static class AgentHealthService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

        public static async Task<AgentHealthInfo> CheckAsync(string host, string? apiKey, AppSettings settings, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(apiKey))
            {
                return new AgentHealthInfo
                {
                    Status = AgentHealthStatus.Offline,
                    Detail = "Agent anahtarı yapılandırılmamış"
                };
            }

            var uri = AgentEndpointHelper.TryBuildHealthUri(host, settings);
            if (uri == null)
            {
                return new AgentHealthInfo
                {
                    Status = AgentHealthStatus.Offline,
                    Detail = "Geçersiz agent host"
                };
            }

            var sw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.TryAddWithoutValidation("X-Netvium-Key", apiKey);

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                sw.Stop();

                if (!resp.IsSuccessStatusCode)
                {
                    return new AgentHealthInfo
                    {
                        Status = AgentHealthStatus.Offline,
                        LatencyMs = sw.ElapsedMilliseconds,
                        EndpointReachable = false,
                        Detail = $"HTTP {(int)resp.StatusCode}"
                    };
                }

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                string? version = null;
                DateTime? heartbeat = null;

                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                        version = v.GetString();
                    if (root.TryGetProperty("time", out var t))
                    {
                        if (t.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(t.GetString(), out var dto))
                            heartbeat = dto.UtcDateTime;
                        else if (t.TryGetDateTimeOffset(out var dto2))
                            heartbeat = dto2.UtcDateTime;
                    }
                }

                var latency = sw.ElapsedMilliseconds;
                var status = latency > 500
                    ? AgentHealthStatus.Warning
                    : AgentHealthStatus.Online;

                return new AgentHealthInfo
                {
                    Status = status,
                    Version = version ?? "unknown",
                    LastHeartbeatUtc = heartbeat ?? DateTime.UtcNow,
                    LatencyMs = latency,
                    EndpointReachable = true,
                    Detail = status == AgentHealthStatus.Warning ? "Yüksek gecikme" : null
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.Debug($"Agent health check failed for {host}: {ex.Message}", category: "Agent", action: "Health");
                return new AgentHealthInfo
                {
                    Status = AgentHealthStatus.Offline,
                    LatencyMs = sw.ElapsedMilliseconds,
                    EndpointReachable = false,
                    Detail = "Agent health check failed."
                };
            }
        }
    }
}
