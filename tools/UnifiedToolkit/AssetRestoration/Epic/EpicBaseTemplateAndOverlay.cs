namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicBaseTemplate
{
    public string SchemaVersion { get; set; } = "1.1.0";
    public string ImplementationVersion { get; set; } = "15A-R5";
    public DateTimeOffset GeneratedUtc { get; set; }
    public EpicTokenCanvas Canvas { get; set; } = new();
    public EpicTokenMeshAnalysis Mesh { get; set; } = new();
    public EpicTokenImageAnalysis Template { get; set; } = new();
    public EpicBaseTemplateLayout Layout { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
}

public sealed class EpicBaseTemplateLayout
{
    public string Status { get; set; } = "CommonEpicBaseTemplate";
    public string CoordinateSpace { get; set; } = "NormalisedCanvasUv";
    public string Orientation { get; set; } = "ForeAtTopAftAtBottom";
    public bool UsesPhysicalTokenOutline { get; set; }
    public bool UsesPhysicalBaseGuideExtensions { get; set; }
    public bool RequiresTransparentCutouts { get; set; }
    public EpicTokenUvBounds MeshUvBounds { get; set; } = new();
    public EpicBaseTemplateCalibration Calibration { get; set; } = new();
    public List<EpicTokenSemanticSection> Sections { get; set; } = new();
    public EpicTokenPendingLine SectionDivider { get; set; } = new();
    public List<EpicTokenShipMountMarker> ShipMountMarkers { get; set; } = new();
    public List<EpicTokenArtworkRegion> CommonRegions { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class EpicBaseTemplateCalibration
{
    public string Status { get; set; } = "ReferenceCalibrated";
    public string Method { get; set; } =
        "OBJ UV main-surface island plus normalized measurements from a physical Epic token photograph";
    public string ReferenceShipId { get; set; } = "cr90corvette";
    public string ReferencePhotograph { get; set; } =
        "assets/source/unified1e/reference/epic/cr90corvette/photos/full.jpg";
    public EpicTokenImageAnalysis ReferencePhotographAnalysis { get; set; } = new();
    public EpicTokenUvBounds MainRenderedSurface { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class EpicShipOverlay
{
    public string SchemaVersion { get; set; } = "1.1.0";
    public string ImplementationVersion { get; set; } = "15C-R2";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string ShipId { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public string FactionId { get; set; } = string.Empty;
    public string FactionThemeId { get; set; } = string.Empty;
    public string BaseTemplatePath { get; set; } = string.Empty;
    public EpicShipOverlaySources Sources { get; set; } = new();
    public EpicShipWeaponLayout Weapons { get; set; } = new();
    public List<EpicTokenFontAnalysis> Fonts { get; set; } = new();
    public List<string> Photographs { get; set; } = new();
    public List<EpicTokenArtworkRegion> ShipRegions { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class EpicShipOverlaySources
{
    public string ShipIcon { get; set; } = string.Empty;
    public string ActionFont { get; set; } = string.Empty;
    public string ShipFont { get; set; } = string.Empty;
}

public sealed class EpicShipWeaponLayout
{
    public bool HasPrimaryWeapon { get; set; }
    public List<EpicShipFiringArc> FiringArcs { get; set; } = new();
}

public sealed class EpicShipFiringArc
{
    public string Id { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string ArcType { get; set; } = "Primary";
    public string Direction { get; set; } = string.Empty;
    public string CalibrationStatus { get; set; } =
        "PendingShipSpecificCalibration";
    public EpicTokenOptionalPoint? Origin { get; set; }
    public List<EpicTokenOptionalPoint> BoundaryPoints { get; set; } = new();
}

public sealed class EpicBaseTemplateBuildResult
{
    public EpicBaseTemplate Template { get; set; } = new();
    public string TemplatePath { get; set; } = string.Empty;
    public string CalibrationOverlayPath { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
}

public sealed class EpicShipOverlayBuildResult
{
    public EpicShipOverlay Overlay { get; set; } = new();
    public string OverlayPath { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
}
