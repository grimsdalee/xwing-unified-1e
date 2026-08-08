namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicShipTargetingLayoutCatalogue
{
    public string SchemaVersion { get; set; } = "1.1.0";
    public string ImplementationVersion { get; set; } = "15C-R12";
    public DateTimeOffset GeneratedUtc { get; set; }
    public List<EpicShipTargetingLayout> Ships { get; set; } = new();
}

public sealed class EpicShipTargetingLayout
{
    public string ShipId { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public string FactionId { get; set; } = string.Empty;
    public EpicDividerLayout Divider { get; set; } = new();
    public List<EpicTargetingGeometry> TargetingGeometry { get; set; } = new();
    public List<EpicTurretIndicator> TurretIndicators { get; set; } = new();
    public string ReferenceImage { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = new();
}

public sealed class EpicDividerLayout
{
    public bool Visible { get; set; }
    public string ColourRole { get; set; } = "BlueDivider";
}

public sealed class EpicTargetingGeometry
{
    public string Id { get; set; } = string.Empty;
    public string GeometryType { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string ColourRole { get; set; } = "PrimaryArcColour";
    public string FillColourRole { get; set; } = "ArcFillColour";
    public bool FillEnabled { get; set; }
    public bool Dashed { get; set; }
    public string CalibrationStatus { get; set; } = "ReferenceDerived";
}

public sealed class EpicTurretIndicator
{
    public string Id { get; set; } = string.Empty;
    public string Centre { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string ColourRole { get; set; } = "PrimaryArcColour";
    public string CalibrationStatus { get; set; } = "ReferenceDerived";
}

public sealed class EpicShipTargetingTextureResult
{
    public string ImplementationVersion { get; set; } = "15C-R12";
    public string ShipId { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public int GeometryCount { get; set; }
    public int TurretIndicatorCount { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
