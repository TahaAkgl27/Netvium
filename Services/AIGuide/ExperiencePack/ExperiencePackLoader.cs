using System.IO;
using System.Text.Json;
using Netvium.Models.AIGuide.ExperiencePack;

namespace Netvium.Services.AIGuide.ExperiencePack
{
    public static class ExperiencePackLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static ExperiencePackBundle Load(string? rootDirectory = null)
        {
            var baseDir = ResolvePackDirectory(rootDirectory);
            if (!Directory.Exists(baseDir))
                throw new InvalidOperationException($"AI Guide ExperiencePack missing or invalid. Directory not found: {baseDir}");

            var bundle = new ExperiencePackBundle
            {
                WindowsAdmin = LoadRequired<ExperiencePackDocument>(baseDir, "windows-admin-experience.tr.json"),
                ActiveDirectory = LoadRequired<ExperiencePackDocument>(baseDir, "active-directory-experience.tr.json"),
                Dhcp = LoadRequired<ExperiencePackDocument>(baseDir, "dhcp-experience.tr.json"),
                Dns = LoadRequired<ExperiencePackDocument>(baseDir, "dns-experience.tr.json"),
                Iis = LoadRequired<ExperiencePackDocument>(baseDir, "iis-experience.tr.json"),
                FilePermission = LoadRequired<ExperiencePackDocument>(baseDir, "file-permission-experience.tr.json"),
                EventViewer = LoadRequired<ExperiencePackDocument>(baseDir, "event-viewer-experience.tr.json")
            };

            ExperiencePackValidator.Validate(bundle);
            return bundle;
        }

        public static string ResolvePackDirectory(string? rootDirectory = null)
        {
            if (!string.IsNullOrWhiteSpace(rootDirectory))
                return Path.Combine(rootDirectory, "Resources", "AIGuide", "ExperiencePacks");

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Resources", "AIGuide", "ExperiencePacks"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Resources", "AIGuide", "ExperiencePacks")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Resources", "AIGuide", "ExperiencePacks"))
            };

            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }

        private static T LoadRequired<T>(string baseDir, string fileName)
        {
            var path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"AI Guide ExperiencePack missing or invalid. Required file not found: {path}");

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (doc == null)
                throw new InvalidOperationException($"AI Guide ExperiencePack missing or invalid. Failed to parse: {path}");

            return doc;
        }
    }
}
