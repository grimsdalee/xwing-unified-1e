using System.Security.Cryptography;
using SkiaSharp;

namespace UnifiedToolkit.KnowledgeBase.Assets.Images;

public sealed class ImageAnalyzer
{
    public ImageAnalysisResult Analyze(string path, byte visibleAlphaThreshold = 1)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Image file was not found.", path);
        }

        using var stream = File.OpenRead(path);
        using var bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException($"SkiaSharp could not decode the image: {path}");

        var width = bitmap.Width;
        var height = bitmap.Height;
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        long transparent = 0;
        long translucent = 0;
        long opaque = 0;
        var hasAlpha = bitmap.AlphaType != SKAlphaType.Opaque;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                if (alpha == 0) transparent++;
                else if (alpha == 255) opaque++;
                else translucent++;

                if (alpha < visibleAlphaThreshold) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        var visibleWidth = right >= left ? right - left + 1 : 0;
        var visibleHeight = bottom >= top ? bottom - top + 1 : 0;
        if (visibleWidth == 0 || visibleHeight == 0)
        {
            left = 0;
            top = 0;
        }

        var paddingLeft = visibleWidth == 0 ? width : left;
        var paddingRight = visibleWidth == 0 ? 0 : width - right - 1;
        var paddingTop = visibleHeight == 0 ? height : top;
        var paddingBottom = visibleHeight == 0 ? 0 : height - bottom - 1;
        var horizontalOffset = visibleWidth == 0 ? 0 : ((left + right) / 2.0) - ((width - 1) / 2.0);
        var verticalOffset = visibleHeight == 0 ? 0 : ((top + bottom) / 2.0) - ((height - 1) / 2.0);

        var warnings = new List<string>();
        if (!hasAlpha || transparent == 0) warnings.Add("NoTransparency");
        if (visibleWidth == 0 || visibleHeight == 0) warnings.Add("FullyTransparent");
        if (visibleWidth > 0 && left == 0) warnings.Add("TouchesLeftEdge");
        if (visibleWidth > 0 && right == width - 1) warnings.Add("TouchesRightEdge");
        if (visibleHeight > 0 && top == 0) warnings.Add("TouchesTopEdge");
        if (visibleHeight > 0 && bottom == height - 1) warnings.Add("TouchesBottomEdge");
        if (Math.Abs(horizontalOffset) > 2) warnings.Add("HorizontalOffset");
        if (Math.Abs(verticalOffset) > 2) warnings.Add("VerticalOffset");

        return new ImageAnalysisResult
        {
            Path = path,
            Width = width,
            Height = height,
            HasAlphaChannel = hasAlpha,
            TransparentPixels = transparent,
            TranslucentPixels = translucent,
            OpaquePixels = opaque,
            VisibleBounds = new ImageVisibleBounds { Left = left, Top = top, Width = visibleWidth, Height = visibleHeight },
            PaddingLeft = paddingLeft,
            PaddingRight = paddingRight,
            PaddingTop = paddingTop,
            PaddingBottom = paddingBottom,
            HorizontalCentreOffset = horizontalOffset,
            VerticalCentreOffset = verticalOffset,
            TouchesLeftEdge = visibleWidth > 0 && left == 0,
            TouchesRightEdge = visibleWidth > 0 && right == width - 1,
            TouchesTopEdge = visibleHeight > 0 && top == 0,
            TouchesBottomEdge = visibleHeight > 0 && bottom == height - 1,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            Warnings = string.Join(";", warnings)
        };
    }
}
