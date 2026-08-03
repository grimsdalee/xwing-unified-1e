namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicTokenBlueprint
{
    public string SchemaVersion { get; set; } = "3.0.0";
    public string ImplementationVersion { get; set; } = "15A-R3";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string ShipId { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public EpicTokenCanvas Canvas { get; set; } = new();
    public EpicTokenSourceAssets Sources { get; set; } = new();
    public EpicTokenMeshAnalysis Mesh { get; set; } = new();
    public EpicTokenImageAnalysis Template { get; set; } = new();
    public EpicTokenImageAnalysis ShipIcon { get; set; } = new();
    public List<EpicTokenFontAnalysis> Fonts { get; set; } = new();
    public List<string> Photographs { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
    public EpicTokenTextureLayout Layout { get; set; } = new();
}

public sealed class EpicTokenCanvas
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string Shape { get; set; } = "Rectangle";
    public string TextureMode { get; set; } = "MeshOverlay";
}

public sealed class EpicTokenSourceAssets
{
    public string Mesh { get; set; } = string.Empty;
    public string TemplateTexture { get; set; } = string.Empty;
    public string ShipIcon { get; set; } = string.Empty;
    public string ActionFont { get; set; } = string.Empty;
    public string ShipFont { get; set; } = string.Empty;
}

public sealed class EpicTokenMeshAnalysis
{
    public int VertexCount { get; set; }
    public int TextureCoordinateCount { get; set; }
    public int NormalCount { get; set; }
    public int FaceCount { get; set; }
    public int FacesWithTextureCoordinates { get; set; }
    public EpicTokenUvBounds UvBounds { get; set; } = new();
    public List<EpicTokenUvSegment> UvSegments { get; set; } = new();
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class EpicTokenUvBounds
{
    public double MinU { get; set; }
    public double MinV { get; set; }
    public double MaxU { get; set; }
    public double MaxV { get; set; }
}

public sealed class EpicTokenUvPoint
{
    public double U { get; set; }
    public double V { get; set; }
}

public sealed class EpicTokenUvSegment
{
    public EpicTokenUvPoint Start { get; set; } = new();
    public EpicTokenUvPoint End { get; set; } = new();
}

public sealed class EpicTokenImageAnalysis
{
    public string RepositoryPath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class EpicTokenFontAnalysis
{
    public string RepositoryPath { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public bool LoadedSuccessfully { get; set; }
}

public sealed class EpicTokenTextureLayout
{
    public string Status { get; set; } = "RectangularUvTextureFoundation";
    public string CoordinateSpace { get; set; } = "NormalisedCanvasUv";
    public string Orientation { get; set; } = "ForeAtTopAftAtBottom";
    public bool UsesPhysicalTokenOutline { get; set; }
    public bool UsesPhysicalBaseGuideExtensions { get; set; }
    public bool RequiresTransparentCutouts { get; set; }
    public EpicTokenUvBounds MeshUvBounds { get; set; } = new();
    public EpicTokenSemanticSection ForeSection { get; set; } = new()
    {
        Id = "fore",
        Name = "Fore"
    };
    public EpicTokenSemanticSection AftSection { get; set; } = new()
    {
        Id = "aft",
        Name = "Aft"
    };
    public EpicTokenPendingLine SectionDivider { get; set; } = new()
    {
        Id = "fore-aft-divider",
        Purpose = "Printed visual divider between Fore and Aft artwork"
    };
    public List<EpicTokenShipMountMarker> ShipMountMarkers { get; set; } = new();
    public List<EpicTokenArtworkRegion> ArtworkRegions { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class EpicTokenSemanticSection
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CalibrationStatus { get; set; } = "PendingUvCalibration";
    public EpicTokenOptionalBounds? Bounds { get; set; }
}

public sealed class EpicTokenPendingLine
{
    public string Id { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string CalibrationStatus { get; set; } = "PendingUvCalibration";
    public EpicTokenOptionalPoint? Start { get; set; }
    public EpicTokenOptionalPoint? End { get; set; }
}

public sealed class EpicTokenShipMountMarker
{
    public string Id { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Purpose { get; set; } =
        "Printed ship-mount alignment guide; not a physical hole or cut-out";
    public string CalibrationStatus { get; set; } = "PendingUvCalibration";
    public EpicTokenOptionalPoint? Centre { get; set; }
    public double? GuideCircleRadius { get; set; }
}

public sealed class EpicTokenArtworkRegion
{
    public string Id { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string CalibrationStatus { get; set; } = "PendingUvCalibration";
    public EpicTokenOptionalBounds? Bounds { get; set; }
}

public sealed class EpicTokenOptionalPoint
{
    public double U { get; set; }
    public double V { get; set; }
}

public sealed class EpicTokenOptionalBounds
{
    public double MinU { get; set; }
    public double MinV { get; set; }
    public double MaxU { get; set; }
    public double MaxV { get; set; }
}

public sealed class EpicTokenBlueprintBuildResult
{
    public EpicTokenBlueprint Blueprint { get; set; } = new();
    public string BlueprintPath { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
}
