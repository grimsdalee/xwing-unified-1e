namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class UpgradeSourceAlternate
{
    public string SourceId { get; init; } = "";
    public string CanonicalSourceId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetSlot { get; init; } = "";
    public string Relationship { get; init; } = "AlternatePrinting";
    public string Notes { get; init; } = "This Unified source record resolves to the same First Edition upgrade identity as the canonical source record.";
}
