namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class UpgradeMappingCandidate
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourceSlot { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string TargetSlot { get; init; } = "";
    public int TargetCost { get; init; }
    public bool TargetUnique { get; init; }
    public string Status { get; init; } = "";
    public string MatchMethod { get; init; } = "";
    public decimal Confidence { get; init; }
    public string Notes { get; init; } = "";
}
