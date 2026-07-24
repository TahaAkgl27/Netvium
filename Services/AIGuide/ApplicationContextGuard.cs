using System.Text;
using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ApplicationContextGuard
    {
        public static string BuildFingerprint(ScreenVisualContext context)
        {
            var sb = new StringBuilder();
            sb.Append((int)context.MmcConsole);
            sb.Append('|');
            sb.Append(NormalizeToken(context.MmcSnapIn));
            sb.Append('|');
            sb.Append(NormalizeToken(context.ActiveWindowTitle));
            sb.Append('|');
            sb.Append(NormalizeToken(context.ConsoleType));
            return sb.ToString();
        }

        public static bool HasApplicationChanged(string? previousFingerprint, string currentFingerprint)
        {
            if (string.IsNullOrWhiteSpace(previousFingerprint))
                return false;

            return !string.Equals(previousFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        }
    }
}
