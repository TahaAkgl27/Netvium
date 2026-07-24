using System.Text.Json;
using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class VisionObservationParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly Regex SecretPatterns = new(
            @"(password|passwd|pwd|secret|api[_-]?key|token)\s*[:=]\s*\S+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static VisionObservation? TryParse(string? json, out string? normalizedJson)
        {
            normalizedJson = null;
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var payload = ExtractJsonPayload(json.Trim());
            normalizedJson = payload;

            try
            {
                var observation = JsonSerializer.Deserialize<VisionObservation>(payload, JsonOptions);
                if (observation == null)
                    return null;

                NormalizeLists(observation);
                RedactSensitiveContent(observation);
                observation.Safety ??= new VisionSafetyInfo();
                observation.Safety.ReadOnlyAnalysis = true;
                observation.Safety.PerformedActions = false;
                normalizedJson = JsonSerializer.Serialize(observation, JsonOptions);
                return observation;
            }
            catch
            {
                return null;
            }
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

        private static void NormalizeLists(VisionObservation observation)
        {
            observation.DetectedSoftware ??= new List<string>();
            observation.VisibleControls ??= new List<VisibleControl>();
            observation.VisibleText ??= new List<VisibleTextBlock>();
            observation.VisibleErrors ??= new List<VisibleError>();
            observation.VisibleWarnings ??= new List<VisibleError>();
            observation.Limitations ??= new List<string>();
        }

        private static void RedactSensitiveContent(VisionObservation observation)
        {
            observation.WindowTitle = Redact(observation.WindowTitle);
            observation.CurrentTaskHint = Redact(observation.CurrentTaskHint);

            foreach (var block in observation.VisibleText)
                block.Text = Redact(block.Text);

            foreach (var control in observation.VisibleControls)
            {
                control.Label = Redact(control.Label);
            }

            foreach (var err in observation.VisibleErrors)
                err.Message = Redact(err.Message);

            foreach (var warn in observation.VisibleWarnings)
                warn.Message = Redact(warn.Message);
        }

        private static string Redact(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            return SecretPatterns.Replace(value, m =>
            {
                var sep = m.Value.Contains(':') ? ":" : "=";
                var key = m.Value.Split(sep)[0];
                return $"{key}{sep} ***";
            });
        }
    }
}
