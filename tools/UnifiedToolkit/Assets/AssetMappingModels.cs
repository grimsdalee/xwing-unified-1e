namespace UnifiedToolkit.Assets;

public sealed class AssetMappingRecord
{
    public AssetEntityKey Entity { get; init; } = new();
    public string EntityName { get; init; } = "";
    public AssetRole Role { get; init; }
    public string AssetId { get; init; } = "";
    public string AssetName { get; init; } = "";
    public AssetKind AssetKind { get; init; }
    public AssetStructuralClass StructuralClass { get; init; }
    public AssetSourceKind SourceKind { get; init; }
    public string Location { get; init; } = "";
    public decimal Score { get; init; }
    public AssetConfidenceBand ConfidenceBand { get; init; }
    public string ApprovalKind { get; init; } = "Automatic";
    public string Reason { get; init; } = "";
}

public sealed class SharedAssetMappingRecord
{
    public string AssetId { get; init; } = "";
    public string AssetName { get; init; } = "";
    public AssetRole Role { get; init; }
    public AssetKind AssetKind { get; init; }
    public AssetStructuralClass StructuralClass { get; init; }
    public AssetSourceKind SourceKind { get; init; }
    public string Location { get; init; } = "";
    public IReadOnlyList<string> SemanticKeys { get; init; } = Array.Empty<string>();
    public string Reason { get; init; } = "";
}

public sealed class AssetDispositionRecord
{
    public AssetEntityKey Entity { get; init; } = new();
    public string EntityName { get; init; } = "";
    public AssetRole Role { get; init; }
    public bool Required { get; init; }
    public string Status { get; init; } = "PendingReview";
    public string Reason { get; init; } = "";
    public int CandidateCount { get; init; }
    public string RecommendedAssetId { get; init; } = "";
    public decimal? RecommendedScore { get; init; }
}

public sealed class AssetMappingSetManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public string AssetMappingVersion { get; init; } = "0.1.0";
    public string SemanticMappingVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public int ApprovedMappings { get; init; }
    public int SharedAssignments { get; init; }
    public int PendingReview { get; init; }
    public int OptionalNotRequired { get; init; }
    public int MissingRequired { get; init; }
}
