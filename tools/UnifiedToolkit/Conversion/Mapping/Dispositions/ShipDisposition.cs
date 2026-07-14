namespace UnifiedToolkit.Conversion.Mapping.Dispositions;

public sealed class ShipDisposition
{
    public string SourceId { get; init; } = "";
    public ShipDispositionKind Kind { get; init; }
    public string ProposedTargetId { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Notes { get; init; } = "";
}
