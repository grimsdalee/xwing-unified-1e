namespace UnifiedToolkit.Conversion.FirstEdition;

public sealed class ConversionProvenance
{
    public string SourceId { get; init; } = "";
    public string MappingId { get; init; } = "";
    public ConversionKind Kind { get; init; }
    public string MappingVersion { get; init; } = "";
}
