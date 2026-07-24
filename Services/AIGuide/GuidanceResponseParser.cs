using System.Text;
using System.Text.Json;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class GuidanceResponseFormatter
    {
        public static string Format(GuidanceResponse response)
        {
            if (response.IsCompactVisualAnswer)
                return FormatCompact(response);

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(response.WhatICanSee))
            {
                sb.AppendLine(response.WhatICanSee.Trim());
                sb.AppendLine();
            }
            AppendSteps(sb, response.ManualSteps);
            if (!string.IsNullOrWhiteSpace(response.IfYouDoNotSeeIt))
                sb.AppendLine(AIGuideLanguage.T("Alternative: ", "Alternatif: ") + response.IfYouDoNotSeeIt.Trim());
            if (!string.IsNullOrWhiteSpace(response.ImportantNote))
                sb.AppendLine(AIGuideLanguage.T("Safety note: ", "Güvenlik notu: ") + response.ImportantNote.Trim());
            return sb.ToString().TrimEnd();
        }

        private static string FormatCompact(GuidanceResponse response)
        {
            var sb = new StringBuilder();
            sb.AppendLine(response.DirectAnswer.Trim());
            if (response.ManualSteps.Count > 0)
            {
                sb.AppendLine();
                AppendSteps(sb, response.ManualSteps);
            }
            if (!string.IsNullOrWhiteSpace(response.IfYouDoNotSeeIt))
            {
                sb.AppendLine();
                sb.AppendLine(AIGuideLanguage.T("Alternative: ", "Alternatif: ") + response.IfYouDoNotSeeIt.Trim());
            }
            if (!string.IsNullOrWhiteSpace(response.VisibilityNote))
            {
                sb.AppendLine();
                sb.AppendLine(AIGuideLanguage.T("Visibility note: ", "Görünürlük notu: ") + response.VisibilityNote.Trim());
            }
            if (!string.IsNullOrWhiteSpace(response.ImportantNote))
            {
                sb.AppendLine();
                sb.AppendLine(AIGuideLanguage.T("Safety note: ", "Güvenlik notu: ") + response.ImportantNote.Trim());
            }
            return sb.ToString().TrimEnd();
        }

        private static void AppendSteps(StringBuilder sb, IReadOnlyList<string> steps)
        {
            for (var i = 0; i < Math.Min(steps.Count, 5); i++)
                sb.AppendLine($"{i + 1}. {steps[i].Trim()}");
        }
    }

    internal static class GuidanceResponseParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static GuidanceResponse? TryParse(string requestId, string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var payload = ExtractJsonPayload(json.Trim());

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var steps = ReadStringArray(root, "steps");
                if (steps.Count == 0) steps = ReadStringArray(root, "manualSteps");

                var evidence = new List<GuidanceEvidence>();
                if (root.TryGetProperty("visibleEvidence", out var evidenceEl) && evidenceEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in evidenceEl.EnumerateArray().Take(3))
                    {
                        evidence.Add(new GuidanceEvidence
                        {
                            Label = AIGuideSecretRedactor.Redact(GetString(item, "label")),
                            Detail = AIGuideSecretRedactor.Redact(GetString(item, "detail"))
                        });
                    }
                }

                var response = new GuidanceResponse
                {
                    RequestId = requestId,
                    Success = true,
                    DirectAnswer = AIGuideSecretRedactor.Redact(GetString(root, "directAnswer")),
                    CurrentScreen = AIGuideSecretRedactor.Redact(GetString(root, "currentScreen")),
                    WhatICanSee = AIGuideSecretRedactor.Redact(
                        FirstUseful(GetString(root, "visibleSummary"), GetString(root, "whatICanSee"))),
                    ManualSteps = steps,
                    IfYouDoNotSeeIt = AIGuideSecretRedactor.Redact(
                        FirstUseful(GetString(root, "alternative"), GetString(root, "ifYouDoNotSeeIt"))),
                    VisibilityNote = AIGuideSecretRedactor.Redact(GetString(root, "visibilityNote")),
                    ImportantNote = AIGuideSecretRedactor.Redact(
                        FirstUseful(GetString(root, "safetyNote"), GetString(root, "importantNote"))),
                    Confidence = AIGuideSecretRedactor.Redact(GetString(root, "confidence")),
                    VisibleEvidence = evidence
                };
                response.FormattedAnswer = GuidanceResponseFormatter.Format(response);
                return response;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ReadStringArray(JsonElement root, string name)
        {
            var values = new List<string>();
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
                return values;
            foreach (var item in element.EnumerateArray().Take(5))
            {
                var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!string.IsNullOrWhiteSpace(text)) values.Add(AIGuideSecretRedactor.Redact(text));
            }
            return values;
        }

        private static string GetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el)) return string.Empty;
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? string.Empty,
                JsonValueKind.Number => el.GetRawText(),
                _ => string.Empty
            };
        }

        private static string FirstUseful(params string[] values) =>
            values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

        private static string ExtractJsonPayload(string content)
        {
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstBrace = content.IndexOf('{');
                var lastBrace = content.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    return content.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            return content;
        }
    }
}
