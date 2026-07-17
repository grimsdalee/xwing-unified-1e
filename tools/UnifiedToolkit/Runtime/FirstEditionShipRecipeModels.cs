namespace UnifiedToolkit.Runtime;

public sealed class FirstEditionShipRecipeDocument
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string HybridDefinitionsPath { get; set; } = "";
    public string ConstructionRecipesPath { get; set; } = "";
    public string TargetShipId { get; set; } = "";
    public string TargetShipName { get; set; } = "";
    public FirstEditionShipRecipeSummary Summary { get; set; } = new();
    public FirstEditionShipRecipe? Recipe { get; set; }
    public List<string> Findings { get; set; } = new();
}

public sealed class FirstEditionShipRecipeSummary
{
    public bool ShipFound { get; set; }
    public bool ValidFirstEditionBase { get; set; }
    public bool MediumBaseRejected { get; set; }
    public int PilotCount { get; set; }
    public int AppearanceVariantCount { get; set; }
    public int DialAssetCount { get; set; }
    public int ShipReferenceCount { get; set; }
    public int PhysicalBaseTokenCount { get; set; }
    public bool RuntimeRecipeAvailable { get; set; }
    public bool ReadyForReview { get; set; }
}

public sealed class FirstEditionShipRecipe
{
    public string ShipId { get; set; } = "";
    public string ShipName { get; set; } = "";
    public List<string> Factions { get; set; } = new();
    public string FirstEditionBaseSize { get; set; } = "";
    public string Source25BaseSize { get; set; } = "";
    public bool BaseConversionRequired { get; set; }
    public bool MediumRemoved { get; set; }
    public string SelectedPilotId { get; set; } = "";
    public string SelectedPilotName { get; set; } = "";
    public List<FirstEditionRecipePilot> Pilots { get; set; } = new();
    public List<FirstEditionRecipeAppearance> AppearanceVariants { get; set; } = new();
    public FirstEditionRecipeAppearance? SelectedAppearance { get; set; }
    public FirstEditionRuntimeParameters RuntimeParameters { get; set; } = new();
    public FirstEditionEditionAssets EditionAssets { get; set; } = new();
    public List<string> RequiredRuntimeFunctions { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> ReviewNotes { get; set; } = new();
}

public sealed class FirstEditionRecipePilot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Faction { get; set; } = "";
    public int PilotSkill { get; set; }
    public int SquadPointCost { get; set; }
    public bool Unique { get; set; }
}

public sealed class FirstEditionRecipeAppearance
{
    public string VariantId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SourceGuid { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string MeshUrl { get; set; } = "";
    public string DiffuseUrl { get; set; } = "";
    public string NormalUrl { get; set; } = "";
    public string ColliderUrl { get; set; } = "";
    public bool HasMesh { get; set; }
    public bool HasDiffuse { get; set; }
}

public sealed class FirstEditionRuntimeParameters
{
    public string RuntimeSize { get; set; } = "";
    public string BaseMeshPath { get; set; } = "";
    public string BaseTexturePattern { get; set; } = "";
    public string DefaultPegType { get; set; } = "";
    public string PegMeshPath { get; set; } = "";
    public string ShipMeshUrl { get; set; } = "";
    public string ShipDiffuseUrl { get; set; } = "";
    public string ShipNormalUrl { get; set; } = "";
    public string ShipColliderUrl { get; set; } = "";
    public string BasePrototypeSymbol { get; set; } = "CompositeBase_GUID";
    public string BasePrototypeGuid { get; set; } = "8c3322";
    public bool RejectMedium { get; set; } = true;
}

public sealed class FirstEditionEditionAssets
{
    public List<FirstEditionRecipeAsset> Dials { get; set; } = new();
    public List<FirstEditionRecipeAsset> ShipReferences { get; set; } = new();
    public List<FirstEditionRecipeAsset> PhysicalBaseTokens { get; set; } = new();
    public List<FirstEditionRecipeAsset> Cards { get; set; } = new();
}

public sealed class FirstEditionRecipeAsset
{
    public string AssetId { get; set; } = "";
    public string SourceGuid { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string FactionHint { get; set; } = "";
    public int MatchScore { get; set; }
}
