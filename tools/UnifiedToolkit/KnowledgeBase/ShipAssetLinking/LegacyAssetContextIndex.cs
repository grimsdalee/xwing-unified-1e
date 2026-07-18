using System.Text.Json.Nodes;
using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class LegacyAssetContextIndex
{
    private readonly Dictionary<string, List<LegacyAssetContext>> contextsByUrl;

    private LegacyAssetContextIndex(Dictionary<string, List<LegacyAssetContext>> contextsByUrl)
    {
        this.contextsByUrl = contextsByUrl;
    }

    public static LegacyAssetContextIndex Empty { get; } =
        new(new Dictionary<string, List<LegacyAssetContext>>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<LegacyAssetContext> All => contextsByUrl
        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
        .SelectMany(entry => entry.Value)
        .DistinctBy(context => new
        {
            context.SourceUrl,
            context.JsonPath,
            context.PropertyName,
            context.ObjectGuid,
            context.ObjectNickname
        })
        .ToList();

    public static LegacyAssetContextIndex Load(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath) || !File.Exists(savePath))
        {
            return Empty;
        }

        var root = JsonNode.Parse(File.ReadAllText(savePath));
        if (root is null)
        {
            return Empty;
        }

        var contexts = new Dictionary<string, List<LegacyAssetContext>>(StringComparer.OrdinalIgnoreCase);
        Traverse(root, "$", Array.Empty<LegacyObjectIdentity>(), contexts);
        return new LegacyAssetContextIndex(contexts);
    }

    public IReadOnlyList<LegacyAssetContext> Find(KnowledgeBaseAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<LegacyAssetContext>();
        }

        var matches = new List<LegacyAssetContext>();
        foreach (var source in asset.SourceReferences.Where(reference =>
                     reference.SourceSystem.Equals("legacy1e", StringComparison.OrdinalIgnoreCase)))
        {
            var key = NormalizeUrl(source.SourceLocation);
            if (contextsByUrl.TryGetValue(key, out var contexts))
            {
                matches.AddRange(contexts);
            }
        }

        return matches
            .DistinctBy(context => new
            {
                context.SourceUrl,
                context.JsonPath,
                context.PropertyName,
                context.ObjectGuid,
                context.ObjectNickname
            })
            .ToList();
    }

    private static void Traverse(
        JsonNode node,
        string jsonPath,
        IReadOnlyList<LegacyObjectIdentity> ancestors,
        Dictionary<string, List<LegacyAssetContext>> contexts)
    {
        if (node is JsonObject obj)
        {
            var nextAncestors = ancestors;
            var identity = TryCreateIdentity(obj);
            if (identity is not null)
            {
                nextAncestors = ancestors.Append(identity).TakeLast(8).ToList();
            }

            foreach (var property in obj)
            {
                if (property.Value is null)
                {
                    continue;
                }

                var childPath = $"{jsonPath}.{property.Key}";
                if (property.Value is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && IsAssetUrl(text))
                {
                    AddContext(text, childPath, property.Key, nextAncestors, contexts);
                }
                else
                {
                    Traverse(property.Value, childPath, nextAncestors, contexts);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is { } item)
                {
                    Traverse(item, $"{jsonPath}[{index}]", ancestors, contexts);
                }
            }
        }
    }

    private static LegacyObjectIdentity? TryCreateIdentity(JsonObject obj)
    {
        var name = ReadString(obj, "Name");
        var nickname = ReadString(obj, "Nickname");
        var guid = ReadString(obj, "GUID");
        var description = ReadString(obj, "Description");

        if (string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(nickname)
            && string.IsNullOrWhiteSpace(guid))
        {
            return null;
        }

        return new LegacyObjectIdentity(name, nickname, guid, description);
    }

    private static string ReadString(JsonObject obj, string propertyName) =>
        obj[propertyName] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : string.Empty;

    private static void AddContext(
        string url,
        string jsonPath,
        string propertyName,
        IReadOnlyList<LegacyObjectIdentity> ancestors,
        Dictionary<string, List<LegacyAssetContext>> contexts)
    {
        var nearest = ancestors.LastOrDefault();
        var containerText = string.Join(" | ", ancestors
            .SelectMany(item => new[] { item.Name, item.Nickname })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var normalizedUrl = NormalizeUrl(url);
        var context = new LegacyAssetContext
        {
            SourceUrl = normalizedUrl,
            JsonPath = jsonPath,
            PropertyName = propertyName,
            ObjectName = nearest?.Name ?? string.Empty,
            ObjectNickname = nearest?.Nickname ?? string.Empty,
            ObjectGuid = nearest?.Guid ?? string.Empty,
            ObjectDescription = nearest?.Description ?? string.Empty,
            ContainerText = containerText
        };

        if (!contexts.TryGetValue(normalizedUrl, out var entries))
        {
            entries = new List<LegacyAssetContext>();
            contexts[normalizedUrl] = entries;
        }

        entries.Add(context);
    }

    private static bool IsAssetUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeUrl(string value) => value.Trim();

    private sealed record LegacyObjectIdentity(
        string Name,
        string Nickname,
        string Guid,
        string Description);
}

public sealed class LegacyAssetContext
{
    public string SourceUrl { get; init; } = string.Empty;
    public string JsonPath { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public string ObjectName { get; init; } = string.Empty;
    public string ObjectNickname { get; init; } = string.Empty;
    public string ObjectGuid { get; init; } = string.Empty;
    public string ObjectDescription { get; init; } = string.Empty;
    public string ContainerText { get; init; } = string.Empty;

    public string SearchText => string.Join(" ", new[]
    {
        ObjectName,
        ObjectNickname,
        ObjectDescription,
        ContainerText,
        JsonPath
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
