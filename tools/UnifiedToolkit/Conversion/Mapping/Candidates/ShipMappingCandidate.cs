namespace UnifiedToolkit.Conversion.Mapping.Candidates;

public sealed class ShipMappingCandidate
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourceFaction { get; init; } = "";
    public string SourceSize { get; init; } = "";
    public int SourceHull { get; init; }
    public int SourceShields { get; init; }
    public int SourceAgility { get; init; }

    public string SuggestedTargetId { get; init; } = "";
    public string SuggestedTargetName { get; init; } = "";
    public string MatchMethod { get; init; } = "";
    public decimal Confidence { get; init; }
    public string Decision { get; init; } = "";
    public string ExistingMappingId { get; init; } = "";
    public string ExistingConversionKind { get; init; } = "";
    public string Notes { get; init; } = "";
}
