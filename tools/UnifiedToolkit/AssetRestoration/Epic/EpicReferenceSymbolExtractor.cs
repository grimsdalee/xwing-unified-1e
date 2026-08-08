using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicReferenceSymbolExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EpicReferenceSymbolExtractionResult Extract(
        string repositoryRoot,
        string shipId,
        string referenceFolder,
        string symbolId = "fore-turret-arrow")
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        referenceFolder = Path.GetFullPath(referenceFolder);

        if (!shipId.Equals(
                "cr90corvette",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Phase 15C-R10 currently supports only cr90corvette.");
        }

        if (!symbolId.Equals(
                "fore-turret-arrow",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Phase 15C-R10 currently supports only fore-turret-arrow.");
        }

        var sourcePath = FindReferenceImage(referenceFolder);

        using var source = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidDataException(
                $"Could not decode reference image '{sourcePath}'.");

        // Calibrated from the supplied 1780 x 2047 CR90_fore_close image.
        // This crop includes the complete turret symbol.
        var crop = new EpicReferencePixelRectangle
        {
            X = 300,
            Y = 520,
            Width = 1120,
            Height = 1040
        };

        ValidateCrop(source, crop);

        const double mountCentreX = 889.0;
        const double mountCentreY = 1075.0;
        const double outerRadius = 482.0;
        const double innerRadius = 394.0;

        using var mask = new SKBitmap(
            crop.Width,
            crop.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        mask.Erase(SKColors.Transparent);

        var retainedPixels = 0;

        for (var y = 0; y < crop.Height; y++)
        {
            var sourceY = crop.Y + y;

            for (var x = 0; x < crop.Width; x++)
            {
                var sourceX = crop.X + x;
                var colour = source.GetPixel(sourceX, sourceY);

                if (!IsPrintedRed(colour))
                    continue;

                var deltaX = sourceX - mountCentreX;
                var deltaY = sourceY - mountCentreY;
                var radius = Math.Sqrt(
                    deltaX * deltaX + deltaY * deltaY);

                // Keep the two circular outlines.
                var isCircularBoundary =
                    (radius >= innerRadius - 28
                     && radius <= innerRadius + 28)
                    || (radius >= outerRadius - 28
                        && radius <= outerRadius + 28);

                // Keep the complete outlined arrowhead on the right.
                var isArrowHead =
                    sourceX >= 1110
                    && sourceX <= 1450
                    && sourceY >= 850
                    && sourceY <= 1330;

                if (!isCircularBoundary && !isArrowHead)
                    continue;

                // Exclude the two diagonal firing-arc lines crossing the
                // turret symbol. They are approximately 45-degree lines
                // through the calibrated mount centre.
                var diagonalOneDistance =
                    Math.Abs(deltaY - deltaX)
                    / Math.Sqrt(2.0);
                var diagonalTwoDistance =
                    Math.Abs(deltaY + deltaX)
                    / Math.Sqrt(2.0);

                var onFiringArc =
                    (diagonalOneDistance < 18.0
                     || diagonalTwoDistance < 18.0)
                    && !isArrowHead;

                if (onFiringArc)
                    continue;

                if (CountRedNeighbours(
                        source,
                        sourceX,
                        sourceY) < 2)
                {
                    continue;
                }

                mask.SetPixel(
                    x,
                    y,
                    new SKColor(232, 24, 48, 255));
                retainedPixels++;
            }
        }

        if (retainedPixels < 5000)
        {
            throw new InvalidDataException(
                "Too few red pixels were retained from the full turret symbol.");
        }

        var outputFolder = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "cr90corvette");

        Directory.CreateDirectory(outputFolder);

        var maskPath = Path.Combine(
            outputFolder,
            "fore-turret-full-reference-r10.png");

        var metadataPath = Path.Combine(
            outputFolder,
            "fore-turret-full-reference-r10.json");

        if (File.Exists(maskPath) || File.Exists(metadataPath))
        {
            throw new IOException(
                "R10 reference outputs already exist. Delete them explicitly " +
                "before regenerating so earlier revisions cannot be overwritten.");
        }

        using (var image = SKImage.FromBitmap(mask))
        using (var encoded = image.Encode(
                   SKEncodedImageFormat.Png,
                   100))
        using (var output = File.Create(maskPath))
        {
            encoded.SaveTo(output);
        }

        var metadata = new EpicReferenceSymbolMetadata
        {
            ImplementationVersion = "15C-R10",
            ShipId = "cr90corvette",
            SymbolId = "fore-turret-arrow",
            SourceImage = sourcePath,
            ReferenceMountCentre = new EpicReferencePixelPoint
            {
                X = mountCentreX,
                Y = mountCentreY
            },
            ReferenceOuterRadius = outerRadius,
            SourceCrop = crop,
            MaskSize = new EpicReferencePixelSize
            {
                Width = crop.Width,
                Height = crop.Height
            }
        };

        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            new UTF8Encoding(false));

        return new EpicReferenceSymbolExtractionResult
        {
            ImplementationVersion = "15C-R10",
            ShipId = "cr90corvette",
            SymbolId = "fore-turret-arrow",
            SourceImagePath = sourcePath,
            MaskPath = maskPath,
            MetadataPath = metadataPath,
            SourceWidth = source.Width,
            SourceHeight = source.Height,
            MaskWidth = crop.Width,
            MaskHeight = crop.Height,
            RetainedPixels = retainedPixels
        };
    }

    private static string FindReferenceImage(
        string referenceFolder)
    {
        if (!Directory.Exists(referenceFolder))
        {
            throw new DirectoryNotFoundException(
                $"Reference folder was not found: {referenceFolder}");
        }

        var preferred = new[]
        {
            "CR90_fore_close.jpg",
            "CR90_fore_close.jpeg",
            "CR90_fore_close.png"
        };

        foreach (var fileName in preferred)
        {
            var path = Path.Combine(referenceFolder, fileName);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            "CR90_fore_close.jpg was not found in the reference folder.");
    }

    private static bool IsPrintedRed(
        SKColor colour)
    {
        var red = colour.Red;
        var green = colour.Green;
        var blue = colour.Blue;

        return red >= 105
            && red - green >= 32
            && red - blue >= 24
            && red >= green * 1.30
            && red >= blue * 1.18;
    }

    private static int CountRedNeighbours(
        SKBitmap bitmap,
        int centreX,
        int centreY)
    {
        var count = 0;

        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                    continue;

                var x = centreX + offsetX;
                var y = centreY + offsetY;

                if (x < 0
                    || y < 0
                    || x >= bitmap.Width
                    || y >= bitmap.Height)
                {
                    continue;
                }

                if (IsPrintedRed(bitmap.GetPixel(x, y)))
                    count++;
            }
        }

        return count;
    }

    private static void ValidateCrop(
        SKBitmap source,
        EpicReferencePixelRectangle crop)
    {
        if (crop.X < 0
            || crop.Y < 0
            || crop.Width <= 0
            || crop.Height <= 0
            || crop.X + crop.Width > source.Width
            || crop.Y + crop.Height > source.Height)
        {
            throw new InvalidDataException(
                "The calibrated CR90 arrow crop is outside the source image.");
        }
    }
}
