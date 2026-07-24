using System.Text;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class SmartSummaryFormatter
    {
        public static string Format(SmartScreenSummary summary)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(summary.ScreenStatus))
            {
                sb.AppendLine(summary.ScreenStatus.Trim());
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(summary.ServerName))
            {
                sb.AppendLine(AIGuideLanguage.T("Current server:", "Geçerli sunucu:"));
                sb.AppendLine(summary.ServerName.Trim());
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(summary.DetectedRole))
            {
                sb.AppendLine(AIGuideLanguage.T("Detected role:", "Algılanan rol:"));
                sb.AppendLine(summary.DetectedRole.Trim());
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(summary.Application))
            {
                sb.AppendLine(AIGuideLanguage.T("Application:", "Uygulama:"));
                sb.AppendLine(summary.Application.Trim());
                sb.AppendLine();
            }

            if (summary.VisibleItems.Count > 0)
            {
                sb.AppendLine(AIGuideLanguage.T("I can see:", "Gördüklerim:"));
                foreach (var item in summary.VisibleItems)
                    sb.AppendLine($"* {item.Trim()}");
                sb.AppendLine();
            }

            if (summary.SuggestedTopics.Count > 0)
            {
                sb.AppendLine(AIGuideLanguage.T("You can ask me about:", "Bana şunları sorabilirsiniz:"));
                foreach (var topic in summary.SuggestedTopics)
                    sb.AppendLine($"* {topic.Label}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(summary.Reminder))
            {
                sb.AppendLine(AIGuideLanguage.T("Reminder:", "Hatırlatma:"));
                sb.AppendLine(summary.Reminder.Trim());
            }

            return sb.ToString().TrimEnd();
        }
    }

    internal static class SmartSummaryParser
    {
        public static SmartScreenSummary? TryParse(
            string requestId,
            string? json,
            RuntimeContext? runtimeContext,
            bool lowConfidence)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var payload = ExtractJsonPayload(json.Trim());

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                var root = doc.RootElement;

                var visibleItems = ReadStringArray(root, "visibleItems");
                var topics = ReadTopics(root);
                var evidence = ReadEvidence(root);

                var screenStatus = GetString(root, "screenStatus");
                if (string.IsNullOrWhiteSpace(screenStatus))
                {
                    screenStatus = lowConfidence
                        ? AIGuideLocalizedCatalogs.ScreenStatusLowConfidence()
                        : AIGuideLocalizedCatalogs.ScreenStatusSuccess();
                }

                var summary = new SmartScreenSummary
                {
                    RequestId = requestId,
                    Success = true,
                    ScreenStatus = AIGuideSecretRedactor.Redact(screenStatus),
                    ServerName = runtimeContext?.Server.ServerName,
                    DetectedRole = runtimeContext?.DetectedRole.DisplayName,
                    Application = FirstNonEmpty(
                        runtimeContext?.Application.DetectedApplication,
                        runtimeContext?.Application.WindowTitle),
                    VisibleItems = visibleItems.Select(AIGuideSecretRedactor.Redact).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
                    SuggestedTopics = lowConfidence
                        ? SmartTopicCatalog.Resolve(null, null, true).ToList()
                        : topics,
                    Evidence = evidence,
                    Confidence = runtimeContext?.Confidence,
                    IsLowConfidence = lowConfidence,
                    Reminder = AIGuideLanguage.Reminder
                };

                if (summary.SuggestedTopics.Count == 0)
                {
                    summary.SuggestedTopics.AddRange(
                        SmartTopicCatalog.Resolve(summary.Application, runtimeContext?.Application.WindowTitle, lowConfidence));
                }

                summary.FormattedSummary = SmartSummaryFormatter.Format(summary);
                return summary;
            }
            catch
            {
                return null;
            }
        }

        private static List<SuggestedTopic> ReadTopics(System.Text.Json.JsonElement root)
        {
            var topics = new List<SuggestedTopic>();
            if (!root.TryGetProperty("suggestedTopics", out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
                return topics;

            foreach (var item in el.EnumerateArray())
            {
                var label = AIGuideSecretRedactor.Redact(GetString(item, "label"));
                var question = AIGuideSecretRedactor.Redact(GetString(item, "question"));
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(question))
                    continue;

                topics.Add(new SuggestedTopic { Label = label, Question = question });
            }

            return topics.Take(5).ToList();
        }

        private static List<VisibleEvidenceSummary> ReadEvidence(System.Text.Json.JsonElement root)
        {
            var evidence = new List<VisibleEvidenceSummary>();
            if (!root.TryGetProperty("evidence", out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
                return evidence;

            foreach (var item in el.EnumerateArray())
            {
                evidence.Add(new VisibleEvidenceSummary
                {
                    Label = AIGuideSecretRedactor.Redact(GetString(item, "label")),
                    Detail = AIGuideSecretRedactor.Redact(GetString(item, "detail"))
                });
            }

            return evidence;
        }

        private static List<string> ReadStringArray(System.Text.Json.JsonElement root, string name)
        {
            var values = new List<string>();
            if (!root.TryGetProperty(name, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
                return values;

            foreach (var item in el.EnumerateArray())
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    values.Add(text.Trim());
            }

            return values;
        }

        private static string GetString(System.Text.Json.JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el))
                return string.Empty;

            return el.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => el.GetString() ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

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
