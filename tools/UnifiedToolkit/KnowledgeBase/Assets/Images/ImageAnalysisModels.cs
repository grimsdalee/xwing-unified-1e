namespace UnifiedToolkit.KnowledgeBase.Assets.Images;

public sealed class ImageVisibleBounds
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Right => Width == 0 ? Left : Left + Width - 1;
    public int Bottom => Height == 0 ? Top : Top + Height - 1;
}

public sealed class ImageAnalysisResult
{
    public string Path { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public bool HasAlphaChannel { get; init; }
    public long TransparentPixels { get; init; }
    public long TranslucentPixels { get; init; }
    public long OpaquePixels { get; init; }
    public ImageVisibleBounds VisibleBounds { get; init; } = new();
    public int PaddingLeft { get; init; }
    public int PaddingRight { get; init; }
    public int PaddingTop { get; init; }
    public int PaddingBottom { get; init; }
    public double HorizontalCentreOffset { get; init; }
    public double VerticalCentreOffset { get; init; }
    public bool TouchesLeftEdge { get; init; }
    public bool TouchesRightEdge { get; init; }
    public bool TouchesTopEdge { get; init; }
    public bool TouchesBottomEdge { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string Warnings { get; init; } = string.Empty;
}
