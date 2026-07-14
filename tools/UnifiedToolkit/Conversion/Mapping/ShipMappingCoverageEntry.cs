namespace UnifiedToolkit.Conversion.Mapping;

public sealed class ShipMappingCoverageEntry
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourceSize { get; init; } = "";
    public string Status { get; init; } = "";
    public string MappingId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetName { get; init; } = "";
    public ConversionKind? Kind { get; init; }
    public string Disposition { get; init; } = "";
    public string Reason { get; init; } = "";
}
