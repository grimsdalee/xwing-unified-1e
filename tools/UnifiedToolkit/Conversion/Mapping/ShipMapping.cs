namespace UnifiedToolkit.Conversion.Mapping;

public sealed class ShipMapping
{
    public string MappingId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public ConversionKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public int Attack { get; init; }
    public int Agility { get; init; }
    public int Hull { get; init; }
    public int Shields { get; init; }
    public string ExclusionReason { get; init; } = "";
    public List<string> Actions { get; init; } = new();
    public List<string> Factions { get; init; } = new();
}
