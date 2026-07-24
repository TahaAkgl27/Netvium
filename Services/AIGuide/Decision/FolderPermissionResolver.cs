using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    internal static class FolderPermissionResolver
    {
        public static AdminUserIntent Resolve(string question)
        {
            if (!FolderPermissionExtractor.IsPermissionQuestion(question))
                return AdminUserIntent.Unknown;

            var rules = KnowledgePackHost.Current.IntentPatterns.FolderPermissionExtraction;
            var normalizedQuestion = question ?? string.Empty;
            var q = " " + normalizedQuestion.ToLowerInvariant() + " ";

            if (rules.Blockers.Any(b => q.Contains(b, StringComparison.Ordinal)))
                return AdminUserIntent.Unknown;

            if (q.Contains("proxy", StringComparison.Ordinal) && q.Contains("address", StringComparison.Ordinal))
                return AdminUserIntent.Unknown;

            if (q.Contains("address", StringComparison.Ordinal)
                && !HasFolderContext(q, rules)
                && FolderPermissionExtractor.ExtractPath(normalizedQuestion) == null)
                return AdminUserIntent.Unknown;

            if (FolderPermissionExtractor.ExtractPath(normalizedQuestion) != null)
                return AdminUserIntent.FolderPermission;

            if (q.Contains("kullanıcı", StringComparison.Ordinal) && q.Contains("izin", StringComparison.Ordinal))
                return AdminUserIntent.Unknown;

            if (HasFolderContext(q, rules) || FolderPermissionExtractor.HasExplicitPermissionLevel(normalizedQuestion))
                return AdminUserIntent.FolderPermission;

            if (!string.IsNullOrWhiteSpace(FolderPermissionExtractor.ExtractFolderHint(normalizedQuestion)))
                return AdminUserIntent.FolderPermission;

            return AdminUserIntent.Unknown;
        }

        private static bool HasFolderContext(string q, Models.AIGuide.KnowledgePack.FolderPermissionExtractionRules rules) =>
            rules.ContextKeywords.Any(k => q.Contains(k, StringComparison.Ordinal))
            || q.Contains("klasör", StringComparison.Ordinal)
            || q.Contains("klasor", StringComparison.Ordinal)
            || q.Contains("folder", StringComparison.Ordinal)
            || q.Contains("ntfs", StringComparison.Ordinal)
            || q.Contains("paylaşım izni", StringComparison.Ordinal)
            || q.Contains("paylasim izni", StringComparison.Ordinal);
    }
}
