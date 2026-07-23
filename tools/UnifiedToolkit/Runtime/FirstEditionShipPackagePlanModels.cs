namespace UnifiedToolkit.Runtime;

public static class ShipPackageRoles
{
    public const string ShipModel = "ShipModel";
    public const string ShipTexture = "ShipTexture";
    public const string DialTexture = "DialTexture";
    public const string PilotCard = "PilotCard";
    public const string PilotBaseToken = "PilotBaseToken";
    public const string Base = "Base";
    public const string Peg = "Peg";
}

public static class ShipPackageStatuses
{
    public const string Ready = "Ready";
    public const string ReadyWithOptionalAssetsMissing = "ReadyWithOptionalAssetsMissing";
    public const string UnresolvedRequiredAssets = "UnresolvedRequiredAssets";
    public const string AmbiguousRequiredAssets = "AmbiguousRequiredAssets";
    public const string InvalidSemanticData = "InvalidSemanticData";
}

public sealed class FirstEditionShipPackagePlanDocument
{
    public string SchemaVersion { get; set; } = "1.1.0";
    public string ResolverVersion { get; set; } = "Phase11B-1.0.0";
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string RepositoryRoot { get; set; } = "";
    public string MappingVersion { get; set; } = "";
    public string KnowledgeBasePath { get; set; } = "";
    public FirstEditionShipPackagePlanSummary Summary { get; set; } = new();
    public List<FirstEditionShipPackagePlan> Packages { get; set; } = new();
}

public sealed class FirstEditionShipPackagePlanSummary
{
    public int ShipCount { get; set; }
    public int PilotCount { get; set; }
    public int PackageCount { get; set; }
    public int ReadyCount { get; set; }
    public int ReadyWithOptionalAssetsMissingCount { get; set; }
    public int UnresolvedRequiredAssetsCount { get; set; }
    public int AmbiguousRequiredAssetsCount { get; set; }
    public int InvalidSemanticDataCount { get; set; }
    public int RequiredRoleCount { get; set; }
    public int ResolvedRequiredRoleCount { get; set; }
    public int AmbiguousRequiredRoleCount { get; set; }
    public int MissingRequiredRoleCount { get; set; }
    public int RequirementsWithAlternatesCount { get; set; }
}

public sealed class FirstEditionShipPackagePlan
{
    public string PackageId { get; set; } = "";
    public string ShipId { get; set; } = "";
    public string ShipName { get; set; } = "";
    public string PilotId { get; set; } = "";
    public string PilotName { get; set; } = "";
    public string Faction { get; set; } = "";
    public string BaseSize { get; set; } = "";
    public int PilotSkill { get; set; }
    public int SquadPointCost { get; set; }
    public bool Unique { get; set; }
    public List<string> UpgradeSlots { get; set; } = new();
    public List<FirstEditionShipPackageRequirement> Requirements { get; set; } = new();
    public string PackageStatus { get; set; } = ShipPackageStatuses.UnresolvedRequiredAssets;
    public List<string> ValidationErrors { get; set; } = new();
}

public sealed class FirstEditionShipPackageRequirement
{
    public string Role { get; set; } = "";
    public bool Required { get; set; }
    public string ResolutionStatus { get; set; } = "Missing";
    public string ResolutionSource { get; set; } = "";
    public string ResolutionMethod { get; set; } = "";
    public FirstEditionShipPackageAsset? SelectedAsset { get; set; }
    public List<FirstEditionShipPackageAsset> Candidates { get; set; } = new();
    public List<FirstEditionShipPackageAsset> AlternateAssets { get; set; } = new();
    public string Note { get; set; } = "";
}

public sealed class FirstEditionShipPackageAsset
{
    public string AssetId { get; set; } = "";
    public string RepositoryPath { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string AssetType { get; set; } = "";
    public int Score { get; set; }
    public int ResolverScore { get; set; }
    public string Confidence { get; set; } = "";
    public List<string> Reasons { get; set; } = new();
}
