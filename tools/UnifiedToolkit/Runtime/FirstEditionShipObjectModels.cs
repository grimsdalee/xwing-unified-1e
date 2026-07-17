namespace UnifiedToolkit.Runtime;

public enum FirstEditionBaseSize
{
    Small,
    Large,
    Epic
}

public sealed class FirstEditionShipObjectModelDocument
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string HybridDefinitionsPath { get; set; } = "";
    public string ConstructionRecipesPath { get; set; } = "";
    public string TargetShip { get; set; } = "";
    public FirstEditionShipObjectModelSummary Summary { get; set; } = new();
    public FirstEditionShipObjectModel? ObjectModel { get; set; }
    public List<FirstEditionValueAuditEntry> AuditTrail { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> ReviewNotes { get; set; } = new();
}

public sealed class FirstEditionShipObjectModelSummary
{
    public bool RecipeAvailable { get; set; }
    public bool BaseComponentValid { get; set; }
    public bool PegComponentValid { get; set; }
    public bool ShipModelComponentValid { get; set; }
    public bool IdentifierComponentValid { get; set; }
    public bool PilotDialComponentValid { get; set; }
    public bool MediumRejected { get; set; }
    public int AuditEntryCount { get; set; }
    public int ErrorCount { get; set; }
    public bool ReadyForSerializationReview { get; set; }
}

public sealed class FirstEditionShipObjectModel
{
    public string ShipId { get; set; } = "";
    public string ShipName { get; set; } = "";
    public string PilotId { get; set; } = "";
    public string PilotName { get; set; } = "";
    public List<string> Factions { get; set; } = new();
    public FirstEditionBaseComponent Base { get; set; } = new();
    public FirstEditionPegComponent Peg { get; set; } = new();
    public FirstEditionShipModelComponent ShipModel { get; set; } = new();
    public FirstEditionIdentifierComponent Identifier { get; set; } = new();
    public FirstEditionPilotDialComponent PilotDial { get; set; } = new();
}

public sealed class FirstEditionBaseComponent
{
    public FirstEditionBaseSize Size { get; set; }
    public string RuntimeSize { get; set; } = "";
    public string PrototypeSymbol { get; set; } = "";
    public string PrototypeGuid { get; set; } = "";
    public string MeshPath { get; set; } = "";
    public string TexturePattern { get; set; } = "";
    public string Source25BaseSize { get; set; } = "";
    public bool ConversionRequired { get; set; }
    public bool MediumRemoved { get; set; }
    public bool IsValid { get; set; }
}

public sealed class FirstEditionPegComponent
{
    public string PegType { get; set; } = "";
    public string MeshPath { get; set; } = "";
    public bool IsValid { get; set; }
}

public sealed class FirstEditionShipModelComponent
{
    public string AppearanceVariantId { get; set; } = "";
    public string AppearanceName { get; set; } = "";
    public string SourceGuid { get; set; } = "";
    public string MeshUrl { get; set; } = "";
    public string DiffuseUrl { get; set; } = "";
    public string NormalUrl { get; set; } = "";
    public string ColliderUrl { get; set; } = "";
    public bool IsValid { get; set; }
}

public sealed class FirstEditionIdentifierComponent
{
    public string BaseSize { get; set; } = "";
    public string RuntimeFunction { get; set; } = "shipIdCustomObjectForSize";
    public string ConfigurationFunction { get; set; } = "spawnShipIdentifiersAndConfig";
    public bool IsValid { get; set; }
}

public sealed class FirstEditionPilotDialComponent
{
    public string PilotId { get; set; } = "";
    public string PilotName { get; set; } = "";
    public List<FirstEditionRecipeAsset> DialAssets { get; set; } = new();
    public List<FirstEditionRecipeAsset> PilotCardAssets { get; set; } = new();
    public List<FirstEditionRecipeAsset> ShipReferenceAssets { get; set; } = new();
    public List<FirstEditionRecipeAsset> PhysicalBaseTokenAssets { get; set; } = new();
    public string RuntimeFunction { get; set; } = "spawnPilotCardAndDial";
    public bool IsValid { get; set; }
}

public sealed class FirstEditionValueAuditEntry
{
    public int Sequence { get; set; }
    public string Stage { get; set; } = "";
    public string Property { get; set; } = "";
    public string Value { get; set; } = "";
    public string Expected { get; set; } = "";
    public bool Valid { get; set; }
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}
