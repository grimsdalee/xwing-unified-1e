namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotSourceAlternate
{
    public string SourceId { get; init; } = "";
    public string CanonicalSourceId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetShipId { get; init; } = "";
    public string TargetFaction { get; init; } = "";
    public string Relationship { get; init; } = "AlternatePrinting";
    public string Notes { get; init; } = "";
}
