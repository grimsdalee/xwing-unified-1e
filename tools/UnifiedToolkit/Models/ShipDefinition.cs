namespace UnifiedToolkit.Models;

public sealed class ShipDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public string Faction { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public int SourceLine { get; init; }

    public List<string> RawLines { get; } = new();
}
