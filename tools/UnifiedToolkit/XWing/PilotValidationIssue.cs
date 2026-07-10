namespace UnifiedToolkit.XWing;

public sealed class PilotValidationIssue
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";

    public string PilotId { get; init; } = "";
    public string PilotName { get; init; } = "";
    public string Faction { get; init; } = "";
    public string ShipType { get; init; } = "";
    public string ShipName { get; init; } = "";
}