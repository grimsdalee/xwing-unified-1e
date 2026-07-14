namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class AmbiguousPilotResolution
{
    public string SourceId { get; init; } = "";
    public AmbiguousPilotResolutionDecision Decision { get; init; }
    public string SelectedTargetId { get; init; } = "";
    public PilotDispositionKind? Disposition { get; init; }
    public string Reason { get; init; } = "";
    public IReadOnlyList<AmbiguousPilotCandidate> Candidates { get; init; } = Array.Empty<AmbiguousPilotCandidate>();
}

public sealed class AmbiguousPilotCandidate
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> UpgradeSlots { get; init; } = Array.Empty<string>();
}
