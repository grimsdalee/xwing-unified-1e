namespace UnifiedToolkit.KnowledgeBase;

public sealed class FirstEditionShipRecord
{
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public List<string> Factions { get; init; } = new();
}

public sealed class KnowledgeBaseShipDomain
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public List<KnowledgeBaseShip> Ships { get; init; } = new();
}

public sealed class KnowledgeBaseShip
{
    public string ShipId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BaseSize { get; init; } = string.Empty;
    public List<string> Factions { get; init; } = new();
    public List<KnowledgeBaseShipAssetRole> AssetRoles { get; init; } = new();
}

public sealed class KnowledgeBaseShipAssetRole
{
    public string Role { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string Status { get; init; } = string.Empty;
    public List<KnowledgeBaseShipAssetCandidate> Candidates { get; init; } = new();
}

public sealed class KnowledgeBaseShipAssetCandidate
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Warehouse { get; init; } = string.Empty;
    public int Score { get; init; }
    public string Confidence { get; init; } = string.Empty;
    public List<string> Reasons { get; init; } = new();
}

public sealed class ShipAssetLinkResult
{
    public int Ships { get; init; }
    public int CandidateLinks { get; init; }
    public int ClearRoles { get; init; }
    public int ReviewRoles { get; init; }
    public int MissingRequiredRoles { get; init; }
    public string OutputRoot { get; init; } = string.Empty;
}
