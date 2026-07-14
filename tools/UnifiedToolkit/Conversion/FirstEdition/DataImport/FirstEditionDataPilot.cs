namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public sealed class FirstEditionDataPilot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> UpgradeSlots { get; init; } = new();
    public string SourceFile { get; init; } = "";
}
