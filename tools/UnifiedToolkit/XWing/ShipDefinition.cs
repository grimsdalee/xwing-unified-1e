namespace UnifiedToolkit.XWing;

public sealed class ShipDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";

    public int Hull { get; init; }
    public int Shield { get; init; }
    public int Agility { get; init; }

    public List<string> Factions { get; } = new();
}