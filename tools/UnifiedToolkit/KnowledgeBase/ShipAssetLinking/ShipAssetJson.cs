using UnifiedToolkit.KnowledgeBase;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

internal static class ShipAssetJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Could not parse {path}");

    public static void Write<T>(string path, T value) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, Options),
            new UTF8Encoding(false));
}
