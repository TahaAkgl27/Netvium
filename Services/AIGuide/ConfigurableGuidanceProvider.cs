using System.Net.Http;
using System.Text;
using System.Text.Json;
using Netvium.Helpers;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Multimodal, observation-only guidance provider. The current PNG is primary evidence;
    /// OCR and deterministic workflow output are supporting context only.
    /// </summary>
    public sealed class ConfigurableGuidanceProvider : IGuidanceProvider
    {
        private static readonly HttpClient Http = new();

        internal const string SystemPrompt =
            """
            You are a visual desktop assistant operating in Observe Only mode.

            Context priority is strict:
            1. The user's current question and intended task.
            2. The attached current screenshot, inspected spatially.
            3. Active application and window title.
            4. Recent conversation, only when it helps the current question.
            5. OCR and workflow hints as supporting evidence only.

            First determine what the user is trying to accomplish. Inspect the screenshot spatially and identify the active application, visible controls, selected items, dialogs, and error messages. Answer the actual question directly.
            Use OCR only as supporting evidence. Never dump raw OCR, table rows, identifiers, GUIDs, personal data, connection strings, passwords, tokens, or activation codes into the answer.
            When a required control is visible, describe its exact visible label and approximate location. When it is not visible or the image is cropped, say so and provide one standard application path as an explicitly inferred alternative.
            You may use safe, common application knowledge for that alternative. Clearly distinguish visible facts from inferred application knowledge.
            Do not invent visible UI elements or unreadable button labels. If the application is uncertain, say that it resembles the likely application.
            In Observe Only mode, never claim to click, type, open, run, change, or complete anything. Give precise manual instructions using imperative language addressed to the user.
            For destructive or high-impact actions such as delete, format, service stop, registry edits, SQL DROP/DELETE/UPDATE, warn the user and require confirmation before the risky step.
            Match the user's language naturally and understand informal spelling.
            For a simple question, return a direct answer plus at most 3-5 short steps, one alternative when useful, and one short visibility note. Do not force repetitive headings or confidence text into the answer.
            Return JSON only matching the requested schema.
            """;

        public async Task<GuidanceResponse> GenerateAsync(
            GuidanceRequest request,
            GuidanceProviderOptions options,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120)));

            try
            {
                var endpoint = OpenAiEndpointResolver.Resolve(options.Endpoint, options.Provider, options.Model);
                var observationJson = request.ObservationJson ?? JsonSerializer.Serialize(request.Observation);
                var payloadJson = BuildRequestPayloadJson(request, options, observationJson);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (options.Provider.Equals("Azure OpenAI", StringComparison.OrdinalIgnoreCase))
                    httpRequest.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
                else
                    httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");

                httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token).ConfigureAwait(false);

                var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning(
                        $"Guidance API returned HTTP {(int)response.StatusCode}.",
                        category: "AIGuide",
                        action: "GuidanceHttp");
                    return GuidanceResponse.Failed(request.RequestId, $"Guidance API error (HTTP {(int)response.StatusCode}).");
                }

                var modelJson = ExtractModelContentJson(responseText);
                var parsed = GuidanceResponseParser.TryParse(request.RequestId, modelJson);
                if (parsed == null)
                    return GuidanceResponse.Failed(request.RequestId, "Guidance API response was not valid JSON.");

                if (request.RuntimeContext != null)
                {
                    parsed.ConfidenceDetail = request.RuntimeContext.Confidence;
                    parsed.Confidence = request.RuntimeContext.Confidence.OverallLabel;
                }

                return VisualGuidanceResponseGuard.Apply(parsed, request);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return GuidanceResponse.Failed(request.RequestId, "Guidance request timed out.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Guidance generation failed.", ex, category: "AIGuide", action: "Guidance");
                return GuidanceResponse.Failed(request.RequestId, "Guidance generation failed.");
            }
        }

        internal static string BuildRequestPayloadJson(
            GuidanceRequest request,
            GuidanceProviderOptions options,
            string? observationJson = null)
        {
            var prompt = BuildUserPrompt(request, observationJson ?? JsonSerializer.Serialize(request.Observation));
            object userContent = prompt;
            if (request.HasScreenshot)
            {
                var base64 = Convert.ToBase64String(request.ScreenshotImageBytes!);
                userContent = new object[]
                {
                    new { type = "text", text = prompt },
                    new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:image/png;base64,{base64}", detail = "high" }
                    }
                };
            }

            var body = new
            {
                model = options.Model,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt + "\n\n" + AIGuideLanguage.BuildResponseLanguageRule(request.UserQuestion) },
                    new { role = "user", content = userContent }
                },
                max_tokens = 1200,
                temperature = 0.15,
                response_format = new { type = "json_object" }
            };
            return JsonSerializer.Serialize(body);
        }

        internal static string BuildUserPrompt(GuidanceRequest request, string observationJson)
        {
            var intent = VisualQuestionIntentClassifier.Classify(request.UserQuestion);
            var application = FirstUseful(request.VisualContext?.MmcSnapIn, request.Observation.Application, request.VisualContext?.ConsoleType, "Unknown");
            var windowTitle = FirstUseful(request.VisualContext?.ActiveWindowTitle, request.VisualContext?.OcrWindowTitle, request.Observation.WindowTitle, "Unknown");

            var sb = new StringBuilder();
            sb.AppendLine("UserQuestion:");
            sb.AppendLine(AIGuideSecretRedactor.Redact(request.UserQuestion));
            sb.AppendLine();
            sb.AppendLine("Screenshot/Image:");
            sb.AppendLine(request.HasScreenshot
                ? $"Attached current PNG at {request.ScreenshotWidth}x{request.ScreenshotHeight}; inspect it spatially as primary evidence."
                : "Unavailable; be explicit that visual confirmation is not possible.");
            sb.AppendLine();
            sb.AppendLine("ActiveApplication:");
            sb.AppendLine(AIGuideSecretRedactor.Redact(application));
            sb.AppendLine("WindowTitle:");
            sb.AppendLine(AIGuideSecretRedactor.Redact(windowTitle));
            sb.AppendLine();
            sb.AppendLine("RecentConversation:");
            sb.AppendLine(BuildRecentConversation(request.PriorTurns, intent));
            sb.AppendLine();
            sb.AppendLine("GuideMode:");
            sb.AppendLine(request.GuideMode);
            sb.AppendLine("AvailableCapabilities:");
            sb.AppendLine("- Inspect the attached screenshot and describe visible UI.");
            sb.AppendLine("- Give precise manual guidance.");
            sb.AppendLine("- Use common application knowledge only as a clearly labeled alternative.");
            sb.AppendLine("- Cannot click, type, execute commands, or change the computer.");
            sb.AppendLine();
            sb.AppendLine("DetectedQuestionIntent:");
            sb.AppendLine(intent.ToString());
            sb.AppendLine(VisualQuestionIntentClassifier.PromptDirective(intent));
            if (intent == VisualQuestionIntent.VerifyCurrentScreen)
            {
                sb.AppendLine("Verified current-screen constraint:");
                sb.AppendLine($"The CURRENT screenshot detector identifies: {AIGuideSecretRedactor.Redact(application)}.");
                sb.AppendLine("Previous assistant screen claims are intentionally omitted. Do not identify another application unless the current screenshot itself proves it.");
            }
            sb.AppendLine();
            sb.AppendLine("OCRText (supporting evidence only; never copy as a dump):");
            sb.AppendLine(BuildSupportingOcr(request));
            sb.AppendLine();
            sb.AppendLine("VisionObservation (supporting structured evidence):");
            sb.AppendLine(Truncate(AIGuideSecretRedactor.RedactUntrusted(observationJson), 5000));

            if (request.WorkflowContext != null && request.WorkflowDecision != null)
            {
                sb.AppendLine();
                sb.AppendLine("WorkflowSafetyHints (lower priority than the question and screenshot; do not force a generic step):");
                sb.AppendLine(JsonSerializer.Serialize(new
                {
                    intent = request.WorkflowContext.Intent.ToString(),
                    request.WorkflowContext.IsWrongScreen,
                    request.WorkflowContext.IsClarification,
                    request.WorkflowDecision.ShouldRefuse,
                    suggestedStep = AIGuideSecretRedactor.RedactUntrusted(request.WorkflowDecision.NextStep.Instruction)
                }));
            }

            sb.AppendLine();
            sb.AppendLine("Response rules:");
            sb.AppendLine("- Put the direct answer in directAnswer.");
            sb.AppendLine("- Use steps only when useful; maximum 5.");
            sb.AppendLine("- Put one standard inferred route in alternative only when the control is not visible/cropped or another safe route helps.");
            sb.AppendLine("- visibilityNote must say what is or is not actually visible; keep it short.");
            sb.AppendLine("- visibleEvidence is diagnostic only: at most 3 short UI-level facts, never raw OCR values.");
            sb.AppendLine("- Set safetyNote only for a real risk. Do not add a repetitive Observe Only disclaimer.");
            sb.AppendLine();
            sb.AppendLine("Return JSON only:");
            sb.AppendLine("""
                {
                  "directAnswer": "",
                  "steps": [""],
                  "alternative": "",
                  "visibilityNote": "",
                  "visibleSummary": "",
                  "safetyNote": "",
                  "confidence": "High|Medium|Low",
                  "visibleEvidence": [{ "label": "", "detail": "" }]
                }
                """);
            return sb.ToString();
        }

        internal static string BuildRecentConversation(
            IReadOnlyList<ConversationTurn> turns,
            VisualQuestionIntent intent)
        {
            if (turns.Count == 0) return "None.";

            var relevantTurns = intent == VisualQuestionIntent.VerifyCurrentScreen
                ? turns.Where(turn => turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase)).TakeLast(4)
                : turns.TakeLast(4);

            return string.Join("\n", relevantTurns.Select(turn =>
                $"{(turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User")}: " +
                Truncate(AIGuideSecretRedactor.Redact(turn.Content), 400)));
        }

        private static string BuildSupportingOcr(GuidanceRequest request)
        {
            var items = new List<string>();
            var visual = request.VisualContext;
            if (visual != null)
            {
                Add(items, "Title", visual.OcrWindowTitle);
                Add(items, "Dialog", visual.ModalTitle);
                Add(items, "Dialog body", visual.ModalBodyText);
                Add(items, "Selected item", visual.SelectedListItem);
                Add(items, "Status", visual.OcrStatusBar);
                foreach (var control in visual.DetectedControls.Take(8))
                    Add(items, "Control", $"{control.Label} ({control.Location})");
            }

            foreach (var block in request.Observation.VisibleText
                         .Where(x => x.Confidence >= 0.45)
                         .Take(10))
                Add(items, "Visible text", block.Text);

            var result = items.Count == 0 ? "No reliable OCR text." : string.Join("\n", items);
            return Truncate(AIGuideSecretRedactor.RedactUntrusted(result), 1800);
        }

        private static void Add(List<string> items, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) items.Add($"{label}: {value.Trim()}");
        }

        private static string FirstUseful(params string?[] values) =>
            values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max].TrimEnd() + "…";

        private static string? ExtractModelContentJson(string responseText)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }
            catch
            {
                return null;
            }
        }
    }
}
