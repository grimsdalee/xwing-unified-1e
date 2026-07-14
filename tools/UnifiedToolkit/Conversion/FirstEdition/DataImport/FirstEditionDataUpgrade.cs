namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public sealed class FirstEditionDataUpgrade
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> Factions { get; init; } = new();
    public List<string> ShipRestrictions { get; init; } = new();
    public List<string> SizeRestrictions { get; init; } = new();
    public string Text { get; init; } = "";
    public string SourceFile { get; init; } = "";
}
