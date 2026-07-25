namespace UnifiedToolkit.Conversion.Mapping.Pilots;

/// <summary>
/// An authoritative First Edition pilot that has no corresponding Unified 2.5 source pilot.
/// </summary>
public sealed class OfficialFirstEditionPilot
{
    public string ImportId { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> UpgradeSlots { get; init; } = Array.Empty<string>();
    public string SourceDataset { get; init; } = "xwing-data";
    public string SourceFile { get; init; } = "";
}
