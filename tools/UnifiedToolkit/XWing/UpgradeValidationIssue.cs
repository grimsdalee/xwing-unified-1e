namespace UnifiedToolkit.XWing;

public sealed class UpgradeValidationIssue
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";

    public string UpgradeId { get; init; } = "";
    public string UpgradeName { get; init; } = "";
    public string Slot { get; init; } = "";
    public string FieldName { get; init; } = "";
}