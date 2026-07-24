using System.Net.Http;
using System.Text;
using System.Text.Json;
using Netvium.Helpers;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// OpenAI-compatible vision provider. Sends in-memory PNG only; never logs image bytes or API keys.
    /// </summary>
    public sealed class ConfigurableVisionProvider : IVisionProvider
    {
        private static readonly HttpClient Http = new();

        private const string SystemPrompt =
            """
            You are a read-only screen observation API for Netvium AI Guide.
            Return JSON only matching the required schema.
            Describe ONLY what is visibly present in the screenshot.
            Do NOT infer hidden UI, menus, or off-screen content.
            Do NOT reuse or assume content from any prior screen or conversation.
            When ScreenVisualContext OCR hints are provided, use them only as supporting text evidence for window title, selected items, status bar, and dialog text.
            The screenshot remains primary. Use OCR to disambiguate readable text, never as a replacement for spatial visual inspection and never dump raw OCR rows.
            If OCR confidence is low, lower your confidence and add limitations — do not invent missing text.
            If Event ID 1074 is indicated in OCR hints or visibly present, mention Event ID 1074 explicitly in visibleText and currentTaskHint.
            If an event description is visible or provided by OCR, summarize it in visibleText.
            Do NOT provide instructions, guidance, or tell the user what to click.
            Do NOT assume administrator intent or suggest actions.
            Do NOT include passwords, secrets, or credentials — redact as "***" if visible.
            If the screenshot is blank, black, incomplete, or affected by RDP capture limitations, add that to limitations and lower confidence.
            Set safety.readOnlyAnalysis=true and safety.performedActions=false always.
            currentTaskHint must describe visible context only, not user guidance.
            """;

        public async Task<VisionAnalysisResult> AnalyzeAsync(
            VisionRequest request,
            VisionProviderOptions options,
            CancellationToken cancellationToken = default)
        {
            var screen = request.Screen;
            var source = screen.Source;

            if (screen.ImageBytes == null || screen.ImageBytes.Length == 0)
            {
                return VisionAnalysisResult.Failed(
                    request.RequestId,
                    source,
                    "Screenshot is empty or unavailable.",
                    screen.ImageBytes?.Length ?? 0,
                    AIGuideFailureStage.BlankCapture,
                    safeDetail: "Capture did not produce image bytes for vision analysis.");
            }

            AIGuideAnalysisLogger.VisionRequestStarted(0);
            var visionStarted = DateTime.UtcNow;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120)));

            try
            {
                var endpoint = OpenAiEndpointResolver.Resolve(options.Endpoint, options.Provider, options.Model);
                var base64 = Convert.ToBase64String(screen.ImageBytes);
                var userText = BuildUserPrompt(request, options.CaptureWarning);

                var body = new
                {
                    model = options.Model,
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt + "\n\n" + AIGuideLanguage.BuildResponseLanguageRule() },
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = userText },
                                new
                                {
                                    type = "image_url",
                                    image_url = new { url = $"data:image/png;base64,{base64}", detail = "high" }
                                }
                            }
                        }
                    },
                    max_tokens = 1200,
                    temperature = 0.1,
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
                        $"Vision API returned HTTP {(int)response.StatusCode}.",
                        category: "AIGuide",
                        action: "VisionHttp");
                    AIGuideAnalysisLogger.VisionFailed(0, $"HTTP {(int)response.StatusCode}");
                    return VisionAnalysisResult.Failed(
                        request.RequestId,
                        source,
                        $"Vision API error (HTTP {(int)response.StatusCode}).",
                        screen.ImageBytes.Length,
                        AIGuideFailureStage.VisionApiError,
                        (int)response.StatusCode,
                        "The vision provider rejected the request. Check settings and connectivity.");
                }

                var modelJson = ExtractModelContentJson(responseText);
                if (string.IsNullOrWhiteSpace(modelJson))
                {
                    AIGuideAnalysisLogger.VisionFailed(0, "Empty response body");
                    return VisionAnalysisResult.Failed(
                        request.RequestId,
                        source,
                        "Vision API returned an empty response.",
                        screen.ImageBytes.Length,
                        AIGuideFailureStage.VisionApiError,
                        safeDetail: "The model returned no content to parse.");
                }

                var observation = VisionObservationParser.TryParse(modelJson, out var normalizedJson);
                if (observation == null)
                {
                    AIGuideAnalysisLogger.VisionFailed(0, "JSON parse failed");
                    return VisionAnalysisResult.Failed(
                        request.RequestId,
                        source,
                        "Vision API response was not valid JSON.",
                        screen.ImageBytes.Length,
                        AIGuideFailureStage.VisionJsonParse,
                        safeDetail: "The model response could not be parsed into an observation.");
                }

                if (!string.IsNullOrWhiteSpace(options.CaptureWarning)
                    && !observation.Limitations.Contains(options.CaptureWarning, StringComparer.OrdinalIgnoreCase))
                {
                    observation.Limitations.Add(options.CaptureWarning);
                }

                AIGuideAnalysisLogger.VisionRequestCompleted(0, (long)(DateTime.UtcNow - visionStarted).TotalMilliseconds);

                return new VisionAnalysisResult
                {
                    RequestId = request.RequestId,
                    Success = true,
                    Observation = observation,
                    RawJson = normalizedJson ?? modelJson,
                    CaptureSource = source,
                    AnalyzedAtUtc = DateTime.UtcNow,
                    ImageBytesLength = screen.ImageBytes.Length
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AIGuideAnalysisLogger.VisionFailed(0, "Timeout");
                return VisionAnalysisResult.Failed(
                    request.RequestId,
                    source,
                    "Vision analysis timed out.",
                    screen.ImageBytes.Length,
                    AIGuideFailureStage.VisionTimeout,
                    safeDetail: "The vision request exceeded the configured timeout.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Vision analysis failed.", ex, category: "AIGuide", action: "Vision");
                AIGuideAnalysisLogger.VisionFailed(0, ex.Message);
                return VisionAnalysisResult.Failed(
                    request.RequestId,
                    source,
                    "Vision analysis failed.",
                    screen.ImageBytes.Length,
                    AIGuideFailureStage.VisionApiError,
                    safeDetail: "An unexpected error occurred during vision analysis.");
            }
        }

        internal static string BuildUserPrompt(VisionRequest request, string? captureWarning)
        {
            var screen = request.Screen;
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(request.UserPrompt))
            {
                sb.AppendLine("UserQuestion focus (understand intent and prioritize relevant visible UI; do not answer yet):");
                sb.AppendLine(AIGuideSecretRedactor.RedactUntrusted(request.UserPrompt));
                sb.AppendLine();
            }
            sb.AppendLine("Analyze this screenshot and return JSON only with this schema:");
            sb.AppendLine("""
                {
                  "application": "",
                  "windowTitle": "",
                  "detectedSoftware": [],
                  "screenType": "",
                  "confidence": 0.0,
                  "visibleControls": [{ "type": "", "label": "", "location": "", "confidence": 0.0 }],
                  "visibleText": [{ "text": "", "location": "", "confidence": 0.0 }],
                  "visibleErrors": [{ "message": "", "severity": "", "location": "", "confidence": 0.0 }],
                  "visibleWarnings": [],
                  "currentTaskHint": "",
                  "limitations": [],
                  "safety": { "readOnlyAnalysis": true, "performedActions": false }
                }
                """);
            sb.AppendLine("This is a fresh capture. Ignore any prior screen context.");
            sb.AppendLine($"Capture source hint: {screen.Source}");
            if (!string.IsNullOrWhiteSpace(screen.ServerName))
                sb.AppendLine($"Server context (metadata only): {AIGuideSecretRedactor.RedactUntrusted(screen.ServerName)}");
            if (!string.IsNullOrWhiteSpace(captureWarning))
                sb.AppendLine($"Capture limitation hint: {AIGuideSecretRedactor.RedactUntrusted(captureWarning)}");

            if (screen.VisualContext != null)
            {
                sb.AppendLine();
                sb.AppendLine("ScreenVisualContext (OCR + detection from THIS capture — authoritative hints):");
                sb.AppendLine(AIGuideSecretRedactor.RedactUntrusted(JsonSerializer.Serialize(screen.VisualContext)));
                sb.AppendLine("Use these hints for application, window title, MMC snap-in, console type, tree selection, list selection, status bar, dialog title, and Event ID 1074 when present.");
                if (screen.VisualContext.HasLowOcrConfidence)
                    sb.AppendLine("OCR confidence is LOW — reduce overall confidence and note uncertainty in limitations.");
            }

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
