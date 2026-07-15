namespace UnifiedToolkit.Assets;

public sealed class AssetCatalogue
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string RepositoryFolder { get; init; } = "";
    public string LegacySavePath { get; init; } = "";
    public IReadOnlyList<AssetRecord> Assets { get; init; } = Array.Empty<AssetRecord>();
}
