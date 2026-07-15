using System.Text;

namespace UnifiedToolkit.Assets;

internal static class AssetText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }
        return builder.ToString();
    }

    public static IReadOnlyList<string> Terms(params string?[] values) => values
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(Normalize)
        .Where(x => x.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
