namespace UnifiedToolkit.Conversion.FirstEdition.Pilots;

public sealed class FirstEditionPilot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> UpgradeSlots { get; init; } = Array.Empty<string>();
    public required ConversionProvenance Provenance { get; init; }
}
