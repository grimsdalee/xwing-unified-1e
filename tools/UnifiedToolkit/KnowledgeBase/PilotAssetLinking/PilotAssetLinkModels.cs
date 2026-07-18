namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class FirstEditionPilotRecord
{
    public string MappingId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
}

public sealed class KnowledgeBasePilotDomain
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public List<KnowledgeBasePilot> Pilots { get; init; } = new();
}

public sealed class KnowledgeBasePilot
{
    public string PilotId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<KnowledgeBasePilotAssetRole> AssetRoles { get; init; } = new();
}

public sealed class KnowledgeBasePilotAssetRole
{
    public string Role { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string Status { get; init; } = string.Empty;
    public List<KnowledgeBasePilotAssetCandidate> Candidates { get; init; } = new();
}

public sealed class KnowledgeBasePilotAssetCandidate
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Warehouse { get; init; } = string.Empty;
    public int Score { get; init; }
    public string Confidence { get; init; } = string.Empty;
    public List<string> Reasons { get; init; } = new();
}

public sealed class PilotAssetLinkResult
{
    public int Pilots { get; init; }
    public int CandidateLinks { get; init; }
    public int ClearRoles { get; init; }
    public int ReviewRoles { get; init; }
    public int MissingRequiredRoles { get; init; }
    public string OutputRoot { get; init; } = string.Empty;
}
