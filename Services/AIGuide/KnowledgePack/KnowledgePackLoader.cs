using System.IO;
using System.Text.Json;
using Netvium.Models.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.KnowledgePack
{
    public static class KnowledgePackLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static KnowledgePackBundle Load(string? rootDirectory = null)
        {
            var baseDir = ResolvePackDirectory(rootDirectory);
            if (!Directory.Exists(baseDir))
                throw new InvalidOperationException($"AI Guide KnowledgePack missing or invalid. Directory not found: {baseDir}");

            var bundle = new KnowledgePackBundle
            {
                IntentPatterns = LoadRequired<IntentPatternsDocument>(baseDir, "intent-patterns.tr.json"),
                ScreenCompatibility = LoadRequired<ScreenCompatibilityDocument>(baseDir, "screen-intent-compatibility.json"),
                WorkflowDefinitions = LoadRequired<WorkflowDefinitionsDocument>(baseDir, "workflow-definitions.json"),
                ResponseTemplates = LoadRequired<ResponseTemplatesDocument>(baseDir, "response-templates.tr.json"),
                ConfidenceRules = LoadRequired<ConfidenceRulesDocument>(baseDir, "confidence-rules.json"),
                HallucinationRules = LoadRequired<HallucinationRulesDocument>(baseDir, "hallucination-rules.json"),
                ContradictionRules = LoadRequired<ContradictionRulesDocument>(baseDir, "contradiction-rules.json")
            };

            KnowledgePackValidator.Validate(bundle);
            return bundle;
        }

        public static string ResolvePackDirectory(string? rootDirectory = null)
        {
            if (!string.IsNullOrWhiteSpace(rootDirectory))
                return Path.Combine(rootDirectory, "Resources", "AIGuide", "KnowledgePacks");

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Resources", "AIGuide", "KnowledgePacks"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Resources", "AIGuide", "KnowledgePacks")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Resources", "AIGuide", "KnowledgePacks"))
            };

            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }

        private static T LoadRequired<T>(string baseDir, string fileName)
        {
            var path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"AI Guide KnowledgePack missing or invalid. Required file not found: {path}");

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (doc == null)
                throw new InvalidOperationException($"AI Guide KnowledgePack missing or invalid. Failed to parse: {path}");

            return doc;
        }
    }
}
