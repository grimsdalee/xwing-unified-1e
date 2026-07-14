namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class AmbiguousUpgradeResolution
{
    public string SourceId { get; init; } = "";
    public AmbiguousUpgradeResolutionDecision Decision { get; init; }
    public string SelectedTargetId { get; init; } = "";
    public UpgradeDispositionKind? Disposition { get; init; }
    public string Reason { get; init; } = "";
    public IReadOnlyList<AmbiguousUpgradeCandidate> Candidates { get; init; } = Array.Empty<AmbiguousUpgradeCandidate>();
}

public sealed class AmbiguousUpgradeCandidate
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> Factions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ShipRestrictions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SizeRestrictions { get; init; } = Array.Empty<string>();
    public string Text { get; init; } = "";
}
