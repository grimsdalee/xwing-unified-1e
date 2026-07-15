namespace UnifiedToolkit.Assets;

public sealed class AssetMatchCandidate
{
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string EntityName { get; init; } = "";
    public string EntityShipId { get; init; } = "";
    public string EntityFaction { get; init; } = "";
    public string EntitySlot { get; init; } = "";
    public string SemanticKey { get; init; } = "";
    public AssetRole Role { get; init; }
    public string AssetId { get; init; } = "";
    public string AssetName { get; init; } = "";
    public AssetKind AssetKind { get; init; }
    public AssetStructuralClass StructuralClass { get; init; }
    public AssetSourceKind SourceKind { get; init; }
    public string Location { get; init; } = "";
    public string MatchMethod { get; init; } = "";
    public decimal Confidence { get; init; }
    public decimal RoleScore { get; init; }
    public decimal ContextScore { get; init; }
    public decimal Score { get; init; }
    public AssetConfidenceBand ConfidenceBand { get; init; }
    public bool Recommended { get; init; }
    public string Notes { get; init; } = "";
}
