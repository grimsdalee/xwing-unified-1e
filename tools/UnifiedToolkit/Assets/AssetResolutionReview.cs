namespace UnifiedToolkit.Assets;

public sealed class AssetResolutionReview
{
    public AssetEntityKey Entity { get; init; } = new();
    public string EntityName { get; init; } = "";
    public AssetRole Role { get; init; }
    public bool Required { get; init; } = true;
    public string Decision { get; init; } = "Unreviewed";
    public string SelectedAssetId { get; init; } = "";
    public string Reason { get; init; } = "";
    public IReadOnlyList<AssetResolutionCandidate> Candidates { get; init; } = Array.Empty<AssetResolutionCandidate>();
}

public sealed class AssetResolutionCandidate
{
    public string AssetId { get; init; } = "";
    public string AssetName { get; init; } = "";
    public AssetKind AssetKind { get; init; }
    public AssetStructuralClass StructuralClass { get; init; }
    public AssetSourceKind SourceKind { get; init; }
    public string Location { get; init; } = "";
    public decimal Score { get; init; }
    public AssetConfidenceBand ConfidenceBand { get; init; }
    public string MatchMethod { get; init; } = "";
    public string Notes { get; init; } = "";
}
