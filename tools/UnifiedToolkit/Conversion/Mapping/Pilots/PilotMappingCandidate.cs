namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotMappingCandidate
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourceShipId { get; init; } = "";
    public string SourceFaction { get; init; } = "";
    public int SourceInitiative { get; init; }
    public string TargetId { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string TargetShipId { get; init; } = "";
    public string TargetFaction { get; init; } = "";
    public int TargetPilotSkill { get; init; }
    public int TargetSquadPointCost { get; init; }
    public bool TargetUnique { get; init; }
    public string TargetUpgradeSlots { get; init; } = "";
    public string Status { get; init; } = "";
    public string MatchMethod { get; init; } = "";
    public decimal Confidence { get; init; }
    public string Notes { get; init; } = "";
}
