namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotMappingCoverageEntry
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourceShipId { get; init; } = "";
    public string SourceFaction { get; init; } = "";
    public string Status { get; init; } = "";
    public string CanonicalSourceId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetShipId { get; init; } = "";
    public string TargetFaction { get; init; } = "";
    public string Notes { get; init; } = "";
}
