using System.Globalization;
using System.Text;

namespace Netvium.Services.AIGuide;

internal static class QuestionTextNormalizer
{
    public static string Normalize(string? value)
    {
        var input = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace('\u0131', 'i');

        if (input.Length == 0)
            return string.Empty;

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return string.Join(' ', builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
