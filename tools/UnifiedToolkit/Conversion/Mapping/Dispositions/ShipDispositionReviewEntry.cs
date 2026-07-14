namespace UnifiedToolkit.Conversion.Mapping.Dispositions;

public sealed class ShipDispositionReviewEntry
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourceFactions { get; init; } = "";
    public string SourceSize { get; init; } = "";
    public int SourceHull { get; init; }
    public int SourceShields { get; init; }
    public int SourceAgility { get; init; }
    public ShipDispositionKind Kind { get; init; } = ShipDispositionKind.Unreviewed;
    public string ProposedTargetId { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Notes { get; init; } = "";
}
