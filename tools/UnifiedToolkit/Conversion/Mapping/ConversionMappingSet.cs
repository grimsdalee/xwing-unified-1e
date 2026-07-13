namespace UnifiedToolkit.Conversion.Mapping;

public sealed class ConversionMappingSet
{
    public string Version { get; init; } = "";
    public IReadOnlyList<ShipMapping> Ships { get; init; } = Array.Empty<ShipMapping>();
}
