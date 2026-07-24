using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide
{
    /// <summary>Generic path/user/permission extraction — patterns in code, keywords from Knowledge Pack.</summary>
    internal static class FolderPermissionExtractor
    {
        // Unicode escapes avoid source-encoding corruption of Turkish literals in compiled regexes.
        private const string NameToken = @"[A-Za-z0-9._-]+";
        private const string FolderKeyword = @"(?:klas(?:\u00F6|\u00D6)r|klasor|folder)";

        private static readonly Regex WindowsPathPattern = new(
            @"([A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex UncPathPattern = new(
            @"(\\\\[^\\/\r\n]+\\[^\\/\r\n]+(?:\\[^\\/\r\n]+)*)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FolderBeforeKeywordPattern = new(
            $@"({NameToken})\s*{FolderKeyword}\w*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FolderBeforeIzinPattern = new(
            $@"^({NameToken})\s+izin\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FolderInLocativePattern = new(
            $@"({NameToken})'?(?:de|da)\s",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FolderIzinUserPattern = new(
            $@"^({NameToken})\s+izin\s+({NameToken})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingUserAfterPathPattern = new(
            $@"^({NameToken})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsPermissionQuestion(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return false;

            var keywords = KnowledgePackHost.Current.IntentPatterns.FolderPermissionExtraction.PermissionKeywords;
            var q = " " + question.ToLowerInvariant() + " ";
            if (keywords.Any(k => q.Contains(k, StringComparison.Ordinal)))
                return true;

            return q.Contains(" izni", StringComparison.Ordinal)
                   || q.Contains(" izin ", StringComparison.Ordinal)
                   || q.Contains("ntfs", StringComparison.Ordinal);
        }

        public static string ExtractFolderHint(string question) => ExtractFolderFromQuestion(question);

        public static bool IsDemonstrativeFolderReference(string? name) =>
            IsDemonstrativeFolderName(name ?? string.Empty);

        public static UserGoalContext ExtractGoal(string question, bool useTurkish)
        {
            var path = ExtractPath(question);
            var folderFromPath = ExtractFolderName(path);
            var folderName = !string.IsNullOrWhiteSpace(folderFromPath)
                ? folderFromPath
                : ExtractFolderFromQuestion(question);
            return new UserGoalContext
            {
                TargetPath = path ?? string.Empty,
                TargetFolderName = folderName,
                TargetUser = ExtractTargetUser(question),
                PermissionLevel = ExtractPermissionLevel(question, useTurkish),
                Operation = useTurkish ? "klasör erişim izni verme" : "folder access permission grant",
                HasUserProvidedPath = !string.IsNullOrWhiteSpace(path)
            };
        }

        public static string? ExtractPath(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return null;

            var winMatch = WindowsPathPattern.Match(question);
            if (winMatch.Success)
            {
                var raw = winMatch.Groups[1].Value.TrimEnd('\\', ' ', '.');
                var spaceIdx = raw.IndexOf(' ');
                if (spaceIdx > 0)
                    raw = raw[..spaceIdx].TrimEnd('\\', ' ', '.');
                return raw;
            }

            var uncMatch = UncPathPattern.Match(question);
            return uncMatch.Success ? uncMatch.Groups[1].Value.TrimEnd('\\', ' ', '.') : null;
        }

        public static string ExtractTargetUserPublic(string question) => ExtractTargetUser(question);

        public static bool HasExplicitPermissionLevel(string question)
        {
            var levels = KnowledgePackHost.Current.IntentPatterns.FolderPermissionExtraction.PermissionLevels;
            var q = question.ToLowerInvariant();
            return levels.Any(l => l.Keywords.Any(k => q.Contains(k, StringComparison.Ordinal)));
        }

        private static string ExtractFolderName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            var trimmed = path.TrimEnd('\\');
            var lastSep = trimmed.LastIndexOf('\\');
            return lastSep >= 0 && lastSep < trimmed.Length - 1 ? trimmed[(lastSep + 1)..] : trimmed;
        }

        private static string ExtractFolderFromQuestion(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return string.Empty;

            var match = FolderBeforeKeywordPattern.Match(question);
            if (match.Success && !IsDemonstrativeFolderName(match.Groups[1].Value))
                return match.Groups[1].Value;

            match = FolderBeforeIzinPattern.Match(question);
            if (match.Success && !IsDemonstrativeFolderName(match.Groups[1].Value))
                return match.Groups[1].Value;

            match = FolderInLocativePattern.Match(question);
            return match.Success && !IsDemonstrativeFolderName(match.Groups[1].Value)
                ? match.Groups[1].Value
                : string.Empty;
        }

        private static bool IsDemonstrativeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            return name.Equals("bu", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("su", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("şu", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("this", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("that", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractTargetUser(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return string.Empty;

            var exclusions = KnowledgePackHost.Current.IntentPatterns.FolderPermissionExtraction.UsernameExclusions;

            var folderUserMatch = FolderIzinUserPattern.Match(question);
            if (folderUserMatch.Success)
            {
                var name = folderUserMatch.Groups[2].Value;
                if (!IsExcluded(name, exclusions))
                    return name;
            }

            var path = ExtractPath(question);
            if (!string.IsNullOrWhiteSpace(path))
            {
                var pathIndex = question.IndexOf(path, StringComparison.OrdinalIgnoreCase);
                if (pathIndex >= 0)
                {
                    var remainder = question[(pathIndex + path.Length)..].Trim();
                    var trail = TrailingUserAfterPathPattern.Match(remainder);
                    if (trail.Success)
                    {
                        var name = trail.Groups[1].Value;
                        if (!IsExcluded(name, exclusions))
                            return name;
                    }
                }
            }

            var leadMatch = Regex.Match(question,
                $@"^({NameToken})\s+(?:g(?:\u00F6|\u00D6)rme|gorme|read|yazma|write|izin)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (leadMatch.Success)
            {
                var name = leadMatch.Groups[1].Value;
                if (!IsExcluded(name, exclusions))
                    return name;
            }

            var patterns = new[]
            {
                new Regex($@"({NameToken})\s*(?:kullan(?:\u0131|\u0130)c(?:\u0131|\u0130)s(?:\u0131|\u0130)na|kullanicisina|kullan(?:\u0131|\u0130)c(?:\u0131|\u0130)ya|kullaniciya|'ya|'ye|'a|'e)\s",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                new Regex(@"(?:kullan(?:\u0131|\u0130)c(?:\u0131|\u0130)|kullanici|user)\s+([A-Za-z0-9._-]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                new Regex($@"({NameToken})\s+(?:i(?:\u00E7|\u00C7)in|icin)\s+(?:g(?:\u00F6|\u00D6)rme|gorme|read|yazma|write|izin)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                new Regex($@"(?:i(?:\u00E7|\u00C7)in|icin)\s+({NameToken})\s+(?:read|write|g(?:\u00F6|\u00D6)rme|gorme|yazma|izin)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                new Regex($@"\s({NameToken})\s+(?:read|write|g(?:\u00F6|\u00D6)rme|gorme|yazma|izin)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            };

            foreach (var pattern in patterns)
            {
                var match = pattern.Match(question);
                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim('\'', '"', '.', ',');
                    if (!IsExcluded(name, exclusions))
                        return name;
                }
            }

            return string.Empty;
        }

        private static bool IsExcluded(string name, IReadOnlyList<string> exclusions)
        {
            var lower = name.ToLowerInvariant();
            return exclusions.Any(e => lower == e)
                   || lower.Contains("klas\u00F6r", StringComparison.Ordinal)
                   || lower.Contains("klasor", StringComparison.Ordinal)
                   || lower.Contains("dosya", StringComparison.Ordinal)
                   || lower.Contains("folder", StringComparison.Ordinal);
        }

        private static string ExtractPermissionLevel(string question, bool useTurkish)
        {
            var q = question.ToLowerInvariant();
            var levels = KnowledgePackHost.Current.IntentPatterns.FolderPermissionExtraction.PermissionLevels;
            foreach (var level in levels)
            {
                if (level.Keywords.Any(k => q.Contains(k, StringComparison.Ordinal)))
                    return level.Label.Get(useTurkish);
            }

            return useTurkish ? "Görme / Read" : "Read / List";
        }
    }
}
