namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicReferenceSymbolExtractionResult
{
    public string ImplementationVersion { get; set; } = "15C-R9";
    public string ShipId { get; set; } = string.Empty;
    public string SymbolId { get; set; } = string.Empty;
    public string SourceImagePath { get; set; } = string.Empty;
    public string MaskPath { get; set; } = string.Empty;
    public string MetadataPath { get; set; } = string.Empty;
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int MaskWidth { get; set; }
    public int MaskHeight { get; set; }
    public int RetainedPixels { get; set; }
}

public sealed class EpicReferenceSymbolMetadata
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public string ImplementationVersion { get; set; } = "15C-R9";
    public string ShipId { get; set; } = string.Empty;
    public string SymbolId { get; set; } = string.Empty;
    public string SourceImage { get; set; } = string.Empty;
    public EpicReferencePixelPoint ReferenceMountCentre { get; set; } = new();
    public double ReferenceOuterRadius { get; set; }
    public EpicReferencePixelRectangle SourceCrop { get; set; } = new();
    public EpicReferencePixelSize MaskSize { get; set; } = new();
}

public sealed class EpicReferencePixelPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class EpicReferencePixelRectangle
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class EpicReferencePixelSize
{
    public int Width { get; set; }
    public int Height { get; set; }
}
