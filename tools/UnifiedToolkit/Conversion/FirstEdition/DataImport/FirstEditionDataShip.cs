namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public sealed class FirstEditionDataShip
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public int Attack { get; init; }
    public int Agility { get; init; }
    public int Hull { get; init; }
    public int Shields { get; init; }
    public List<string> Actions { get; init; } = new();
    public List<string> Factions { get; init; } = new();
    public string SourceFile { get; init; } = "";
}
