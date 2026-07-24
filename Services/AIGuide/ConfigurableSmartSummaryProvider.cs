using System.Net.Http;
using System.Text;
using System.Text.Json;
using Netvium.Helpers;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Text-only smart summary provider. Uses VisionObservation + RuntimeContext — never resends screenshots.
    /// </summary>
    public sealed class ConfigurableSmartSummaryProvider : ISmartSummaryProvider
    {
        private static readonly HttpClient Http = new();

        private const string SystemPrompt =
            """
            You are Netvium AI Guide Smart Screen Summary — a read-only observation assistant.
            Generate a short, useful screen summary before the user asks any question.
            Rules:
            - Use VisionObservation for visible UI items only.
            - Use RuntimeContext for server name, role, and telemetry only when present.
            - Never invent visible objects, controls, menus, or applications.
            - Never suggest actions that do not match the detected application.
            - Never say you can perform actions. The user performs all actions manually.
            - Keep the summary concise and useful within 5 seconds of reading.
            - Prefer 3 to 5 suggested topics as questions, not commands.
            - Suggested topics must be phrased as questions the user can ask.
            - If confidence is low or the screen is unclear, state that clearly and do not suggest specific admin actions.
            - Never use "probably", "maybe", or "I think".
            - Never expose passwords or secrets — redact as "***".
            - Never output executable scripts or commands.
            Return JSON only matching the required schema.
            """;

        public async Task<SmartScreenSummary> GenerateAsync(
            SmartSummaryRequest request,
            GuidanceProviderOptions options,
            CancellationToken cancellationToken = default)
        {
            var lowConfidence = SmartSummaryFallbackBuilder.IsLowConfidence(
                request.Observation,
                request.RuntimeContext);

            if (lowConfidence)
            {
                return SmartSummaryFallbackBuilder.Build(
                    request.RequestId,
                    request.Observation,
                    request.RuntimeContext);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120)));

            try
            {
                var endpoint = OpenAiEndpointResolver.Resolve(options.Endpoint, options.Provider, options.Model);
                var observationJson = AIGuideSecretRedactor.RedactUntrusted(request.ObservationJson ?? JsonSerializer.Serialize(request.Observation));

                var body = new
                {
                    model = options.Model,
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt + "\n\n" + AIGuideLanguage.BuildResponseLanguageRule() },
                        new { role = "user", content = BuildUserPrompt(request, observationJson) }
                    },
                    max_tokens = 900,
                    temperature = 0.2,
                    response_format = new { type = "json_object" }
                };

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (options.Provider.Equals("Azure OpenAI", StringComparison.OrdinalIgnoreCase))
                    httpRequest.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
                else
                    httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");

                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json");

                using var response = await Http.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token).ConfigureAwait(false);

                var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning(
                        $"Smart summary API returned HTTP {(int)response.StatusCode}.",
                        category: "AIGuide",
                        action: "SummaryHttp");
                    return SmartSummaryFallbackBuilder.Build(
                        request.RequestId,
                        request.Observation,
                        request.RuntimeContext);
                }

                var modelJson = ExtractModelContentJson(responseText);
                var parsed = SmartSummaryParser.TryParse(
                    request.RequestId,
                    modelJson,
                    request.RuntimeContext,
                    lowConfidence: false);

                if (parsed == null)
                {
                    return SmartSummaryFallbackBuilder.Build(
                        request.RequestId,
                        request.Observation,
                        request.RuntimeContext);
                }

                MergeCatalogTopicsIfNeeded(parsed, request);
                parsed.FormattedSummary = SmartSummaryFormatter.Format(parsed);
                return parsed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return SmartSummaryFallbackBuilder.Build(
                    request.RequestId,
                    request.Observation,
                    request.RuntimeContext);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Smart summary generation failed.", ex, category: "AIGuide", action: "Summary");
                return SmartSummaryFallbackBuilder.Build(
                    request.RequestId,
                    request.Observation,
                    request.RuntimeContext);
            }
        }

        private static void MergeCatalogTopicsIfNeeded(SmartScreenSummary summary, SmartSummaryRequest request)
        {
            if (summary.SuggestedTopics.Count >= 3)
                return;

            var app = summary.Application ?? request.Observation.Application;
            var catalog = SmartTopicCatalog.Resolve(app, request.Observation.WindowTitle, false);
            foreach (var topic in catalog)
            {
                if (summary.SuggestedTopics.Count >= 5)
                    break;

                if (!summary.SuggestedTopics.Any(t =>
                        t.Label.Equals(topic.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    summary.SuggestedTopics.Add(topic);
                }
            }
        }

        private static string BuildUserPrompt(SmartSummaryRequest request, string observationJson)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Generate a smart screen summary from this data.");
            sb.AppendLine();
            sb.AppendLine("VisionObservation JSON:");
            sb.AppendLine(observationJson);

            if (request.RuntimeContext != null)
            {
                sb.AppendLine();
                sb.AppendLine("RuntimeContext JSON:");
                sb.AppendLine(AIGuideSecretRedactor.RedactUntrusted(JsonSerializer.Serialize(request.RuntimeContext)));
            }

            sb.AppendLine();
            sb.AppendLine("Return JSON only with this schema:");
            sb.AppendLine("""
                {
                  "screenStatus": "",
                  "visibleItems": [""],
                  "suggestedTopics": [{ "label": "", "question": "" }],
                  "evidence": [{ "label": "", "detail": "" }]
                }
                """);
            sb.AppendLine("Use application-specific suggested topics when the detected application matches:");
            sb.AppendLine("- Active Directory Users and Computers: reset password, unlock account, create user, group membership");
            sb.AppendLine("- DNS Manager: A record, DNS zone, reverse lookup, name resolution");
            sb.AppendLine("- DHCP Manager: scopes, reservation, leases, scope options");
            sb.AppendLine("- Hyper-V Manager: VM state, start/stop VM, virtual switch, checkpoints");
            sb.AppendLine("- IIS Manager: site status, app pool, bindings, HTTP errors");
            sb.AppendLine("- Event Viewer: Event ID, critical errors, root cause, service crash");
            sb.AppendLine("- Services: service status, restart manually, startup type, stopped service");
            sb.AppendLine("- Task Scheduler: task history, run manually, triggers, failed task");
            sb.AppendLine("- Registry Editor: explain key, value meaning, warn before edits");
            sb.AppendLine("- PowerShell/CMD: explain output, interpret error, safe manual next steps");
            return sb.ToString();
        }

        private static string? ExtractModelContentJson(string responseText)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
            }
            catch
            {
                return null;
            }
        }
    }
}
