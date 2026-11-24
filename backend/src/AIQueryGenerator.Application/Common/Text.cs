using System.Text.RegularExpressions;

namespace AIQueryGenerator.Application.Common;

public static partial class Text
{
    /// <summary>Truncates text for safe display/logging.</summary>
    public static string? Truncate(this string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// Neutralises untrusted descriptive text (schema descriptions) before it is placed in an AI prompt:
    /// strips control characters, collapses whitespace and limits length.
    /// </summary>
    public static string? SanitizeForPrompt(string? value, int max = 200)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = ControlChars().Replace(value, " ");
        cleaned = Whitespace().Replace(cleaned, " ").Trim();
        cleaned = cleaned.Replace("```", "'''").Replace("<", "‹").Replace(">", "›");
        return cleaned.Truncate(max);
    }

    [GeneratedRegex(@"[\p{Cc}\p{Cf}]")]
    private static partial Regex ControlChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
