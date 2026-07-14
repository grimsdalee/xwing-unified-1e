namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class UpgradeMapping
{
    public string MappingId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> Factions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ShipRestrictions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SizeRestrictions { get; init; } = Array.Empty<string>();
    public string Text { get; init; } = "";
}
