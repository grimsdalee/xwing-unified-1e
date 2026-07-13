namespace UnifiedToolkit.XWing;

public sealed class UpgradeRestrictionEntry
{
    public string UpgradeId { get; init; } = "";
    public string UpgradeName { get; init; } = "";
    public string Slot { get; init; } = "";

    public string Path { get; init; } = "";
    public string ValueKind { get; init; } = "";
    public string Value { get; init; } = "";
}