using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Pilots;

namespace UnifiedToolkit.Hybrid;

public sealed class HybridShipDefinitionDocument
{
    public string SchemaVersion { get; init; } = "2.0";
    public string SemanticMappingVersion { get; init; } = "";
    public string GeneratedUtc { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public string UnifiedSavePath { get; init; } = "";
    public string LegacySavePath { get; init; } = "";
    public HybridBuildSummary Summary { get; init; } = new();
    public IReadOnlyList<HybridShipDefinition> Ships { get; init; } = Array.Empty<HybridShipDefinition>();
}

public sealed class HybridBuildSummary
{
    public int ShipCount { get; init; }
    public int FrameworkTemplateCount { get; init; }
    public int LegacyModelObjectCount { get; init; }
    public int LegacyShipFamilyCount { get; init; }
    public int UniqueAppearanceCount { get; init; }
    public int ShipsWithFramework { get; init; }
    public int ShipsWithAppearance { get; init; }
    public int ShipsWithDial { get; init; }
    public int ShipsWithShipReference { get; init; }
    public int ShipsWithPhysicalBaseToken { get; init; }
    public int ShipsFrameworkReady { get; init; }
    public int ShipsAppearanceReady { get; init; }
    public int ShipsEditionAssetsReady { get; init; }
    public int ShipsWithConstructionRecipe { get; init; }
    public int ShipsReadyForObjectBuilder { get; init; }
}

public sealed class HybridShipDefinition
{
    public required FirstEditionShip SemanticData { get; init; }
    public IReadOnlyList<FirstEditionPilot> Pilots { get; init; } = Array.Empty<FirstEditionPilot>();
    public required FirstEditionBaseDefinition BaseDefinition { get; init; }
    public ShipBaseSizeConversion? BaseSizeConversion { get; init; }
    public SpawnerFrameworkReference? SpawnFramework { get; init; }
    public LegacyShipFamilyReference? LegacyShipFamily { get; init; }
    public IReadOnlyList<ShipAppearanceVariant> AppearanceVariants { get; init; } = Array.Empty<ShipAppearanceVariant>();
    public EditionAssetReferences EditionAssets { get; init; } = new();
    public HybridReadiness Readiness { get; init; } = new();
}

public sealed class LegacyShipFamilyReference
{
    public string FamilyId { get; init; } = "";
    public string CanonicalKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string FactionHint { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string DiscoveryMethod { get; init; } = "";
    public int MatchScore { get; init; }
    public IReadOnlyList<string> MatchReasons { get; init; } = Array.Empty<string>();
}

public sealed class SpawnerFrameworkReference
{
    public string FrameworkId { get; init; } = "";
    public string Size { get; init; } = "";
    public string SourceGuid { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public bool HasLua { get; init; }
    public bool HasSnapPoints { get; init; }
    public bool HasContainedObjects { get; init; }
    public bool HasBaseComponent { get; init; }
    public bool HasPegComponent { get; init; }
    public bool HasShipAttachment { get; init; }
    public bool HasSpawnerBehaviour { get; init; }
    public bool IsUtilityObject { get; init; }
    public int DescendantCount { get; init; }
    public int StructuralScore { get; init; }
    public string TemplateJson { get; init; } = "";
}

public sealed class ShipAppearanceVariant
{
    public string VariantId { get; init; } = "";
    public string AppearanceSignature { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourceGuid { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string MeshUrl { get; init; } = "";
    public string DiffuseUrl { get; init; } = "";
    public string NormalUrl { get; init; } = "";
    public string ColliderUrl { get; init; } = "";
    public IReadOnlyList<string> ProvenanceNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProvenanceGuids { get; init; } = Array.Empty<string>();
    public string TemplateJson { get; init; } = "";
}

public sealed class EditionAssetReferences
{
    public IReadOnlyList<EditionAssetReference> ShipReferences { get; init; } = Array.Empty<EditionAssetReference>();
    public IReadOnlyList<EditionAssetReference> PhysicalBaseTokens { get; init; } = Array.Empty<EditionAssetReference>();
    public IReadOnlyList<EditionAssetReference> Dials { get; init; } = Array.Empty<EditionAssetReference>();
    public IReadOnlyList<EditionAssetReference> Cards { get; init; } = Array.Empty<EditionAssetReference>();
}

public sealed class EditionAssetReference
{
    public string AssetId { get; init; } = "";
    public string SourceGuid { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string FactionHint { get; init; } = "";
    public string TemplateJson { get; init; } = "";
    public int MatchScore { get; init; }
    public IReadOnlyList<string> MatchReasons { get; init; } = Array.Empty<string>();
}

public sealed class HybridReadiness
{
    public bool HasSemanticData { get; init; }
    public bool HasValidFirstEditionBase { get; init; }
    public bool HasFramework { get; init; }
    public bool HasConstructionRecipe { get; init; }
    public bool HasAppearance { get; init; }
    public bool HasDial { get; init; }
    public bool HasShipReference { get; init; }
    public bool HasPhysicalBaseToken { get; init; }
    public bool FrameworkReady { get; init; }
    public bool ConstructionRecipeReady { get; init; }
    public bool AppearanceReady { get; init; }
    public bool EditionAssetsReady { get; init; }
    public bool ReadyForObjectBuilder { get; init; }
    public bool CompleteSaveReady { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
