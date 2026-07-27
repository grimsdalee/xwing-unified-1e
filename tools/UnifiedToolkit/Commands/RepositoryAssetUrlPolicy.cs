using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Converts generated references to the imported Unified 2.5 assets in the
/// user's repository. Generated validation objects must never depend directly
/// on raw.githubusercontent.com/JohnnyCheese.
/// </summary>
internal static partial class RepositoryAssetUrlPolicy
{
    private const string UpstreamPrefix =
        "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/";

    public static void RewriteObjectUrls(JsonNode? node, string assetBaseUrl)
    {
        if (node is null)
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Value is JsonValue value
                        && value.TryGetValue<string>(out var text))
                    {
                        obj[property.Key] = RewriteText(text, assetBaseUrl);
                    }
                    else
                    {
                        RewriteObjectUrls(property.Value, assetBaseUrl);
                    }
                }
                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is JsonValue value
                        && value.TryGetValue<string>(out var text))
                    {
                        array[index] = RewriteText(text, assetBaseUrl);
                    }
                    else
                    {
                        RewriteObjectUrls(array[index], assetBaseUrl);
                    }
                }
                break;
        }
    }

    public static string RewriteText(string value, string assetBaseUrl)
    {
        if (string.IsNullOrEmpty(value)
            || !value.Contains(UpstreamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var baseUrl = assetBaseUrl.TrimEnd('/') + "/";

        return JohnnyCheeseRawUrlRegex().Replace(
            value,
            match =>
            {
                var repositoryPath = match.Groups[1].Value.TrimStart('/');
                return baseUrl + "assets/source/unified25/" + repositoryPath;
            });
    }

    [GeneratedRegex(
        "https://raw\\.githubusercontent\\.com/JohnnyCheese/TTS_X-Wing2\\.0/[^/]+/([^\\s\\\"']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JohnnyCheeseRawUrlRegex();
}
