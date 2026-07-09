namespace UnifiedToolkit.Shared;

public static class TextMatch
{
    public static bool Contains(string value, string searchText)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(searchText))
            return false;

        return value.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || Normalise(value).Contains(Normalise(searchText), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalise(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}