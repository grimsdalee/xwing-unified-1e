namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotMapping
{
    public string MappingId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> UpgradeSlots { get; init; } = Array.Empty<string>();
}
