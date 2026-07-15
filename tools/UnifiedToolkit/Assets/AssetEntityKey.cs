namespace UnifiedToolkit.Assets;

public sealed class AssetEntityKey : IEquatable<AssetEntityKey>
{
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public string Slot { get; init; } = "";

    public string SemanticKey => EntityType.ToLowerInvariant() switch
    {
        "pilot" => $"pilot:{EntityId}:{ShipId}:{Faction}",
        "upgrade" => $"upgrade:{EntityId}:{Slot}",
        _ => $"ship:{EntityId}"
    };

    public bool Equals(AssetEntityKey? other) => other is not null &&
        SemanticKey.Equals(other.SemanticKey, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as AssetEntityKey);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(SemanticKey);
}
