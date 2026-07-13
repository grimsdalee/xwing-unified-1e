namespace UnifiedToolkit.Conversion.FirstEdition;

public sealed class FirstEditionShip
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public int Attack { get; init; }
    public int Agility { get; init; }
    public int Hull { get; init; }
    public int Shields { get; init; }
    public List<string> Actions { get; } = new();
    public List<string> Factions { get; } = new();
    public required ConversionProvenance Provenance { get; init; }
}
