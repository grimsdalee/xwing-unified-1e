using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Assets;

internal static class AssetText
{
    private static readonly Regex UnicodeEscape = new(@"\\u(?<hex>[0-9a-fA-F]{4})", RegexOptions.Compiled);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        value = Decode(value);

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }

    public static string Decode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var decoded = UnicodeEscape.Replace(value, match =>
        {
            var code = Convert.ToInt32(match.Groups["hex"].Value, 16);
            return char.ConvertFromUtf32(code);
        });

        decoded = WebUtility.HtmlDecode(decoded);
        return decoded
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('`', '\'')
            .Replace('“', '"')
            .Replace('”', '"');
    }

    public static IReadOnlyList<string> Terms(params string?[] values) => values
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .SelectMany(ExpandTerms)
        .Where(x => x.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IEnumerable<string> ExpandTerms(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var decoded = Decode(value);
        var normalized = Normalize(decoded);
        if (normalized.Length > 0)
            yield return normalized;

        foreach (var token in decoded.Split(new[] { ' ', '\t', '\r', '\n', '/', '\\', '-', '_', ':', ';', ',', '.', '(', ')', '[', ']', '{', '}' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedToken = Normalize(token);
            if (normalizedToken.Length >= 3)
                yield return normalizedToken;
        }
    }
}
