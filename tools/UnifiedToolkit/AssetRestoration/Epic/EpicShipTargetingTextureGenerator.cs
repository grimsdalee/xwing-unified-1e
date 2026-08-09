using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicShipTargetingTextureGenerator
{
    private const float DividerTouchOffsetPixels = 6.0f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static EpicShipTargetingTextureResult Generate(
        string repositoryRoot,
        string shipId,
        string? cataloguePath = null,
        string? templatePath = null,
        string? mountPointPath = null,
        string? commonTexturePath = null,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);

        var catalogue = EpicShipTargetingLayoutBuilder.Load(
            repositoryRoot,
            cataloguePath);

        var layout = catalogue.Ships.SingleOrDefault(
            ship => ship.ShipId.Equals(
                shipId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"No Epic targeting layout exists for '{shipId}'.");

        templatePath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-template.json");

        mountPointPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-mount-points.json");

        commonTexturePath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "epic",
            "common",
            "epic-common-artwork-r4.png");

        ValidateFile(templatePath, "Epic base template");
        ValidateFile(mountPointPath, "Epic mount-point database");
        ValidateFile(commonTexturePath, "Epic common texture");

        var template = Deserialize<EpicBaseTemplate>(templatePath);
        var mountPoints = Deserialize<EpicBaseMountPointDatabase>(
            mountPointPath);
        var theme = EpicFactionThemeCatalogue.Get(layout.FactionId);

        outputPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "epic",
            "targeting",
            $"{layout.ShipId}-targeting-r19.png");

        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (File.Exists(outputPath))
        {
            throw new IOException(
                $"The R19 output already exists and will not be overwritten: {outputPath}");
        }

        using var source = SKBitmap.Decode(commonTexturePath)
            ?? throw new InvalidDataException(
                "Could not decode the Epic common texture.");

        using var bitmap = source.Copy(SKColorType.Rgba8888);
        using var canvas = new SKCanvas(bitmap);

        canvas.Save();
        canvas.ClipRect(
            ToRect(
                template.Layout.Calibration.MainRenderedSurface,
                bitmap.Width,
                bitmap.Height));

        if (layout.Divider.Visible)
        {
            DrawDivider(
                canvas,
                template,
                bitmap.Width,
                bitmap.Height);
        }

        foreach (var geometry in layout.TargetingGeometry)
        {
            DrawGeometry(
                canvas,
                geometry,
                template,
                mountPoints,
                theme,
                bitmap.Width,
                bitmap.Height);
        }

        foreach (var indicator in layout.TurretIndicators)
        {
            DrawTurretIndicator(
                canvas,
                indicator,
                mountPoints,
                theme,
                bitmap.Width,
                bitmap.Height);
        }

        if (layout.ShipId.Equals(
                "cr90corvette",
                StringComparison.OrdinalIgnoreCase))
        {
            DrawCr90PrintedArtwork(
                canvas,
                template,
                bitmap.Width,
                bitmap.Height);
        }

        canvas.Restore();

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Png,
            100);
        using (var output = File.Create(outputPath))
            encoded.SaveTo(output);

        using var stream = File.OpenRead(outputPath);
        var sha256 = Convert.ToHexString(
            SHA256.HashData(stream));

        var reportPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase15",
            "epic-targeting",
            $"{layout.ShipId.ToUpperInvariant()}-TARGETING-R19.md");

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        var result = new EpicShipTargetingTextureResult
        {
            ShipId = layout.ShipId,
            ShipName = layout.ShipName,
            OutputPath = outputPath,
            ReportPath = reportPath,
            GeometryCount = layout.TargetingGeometry.Count,
            TurretIndicatorCount = layout.TurretIndicators.Count,
            Sha256 = sha256
        };

        WriteReport(repositoryRoot, layout, result);
        return result;
    }

    private static void DrawGeometry(
        SKCanvas canvas,
        EpicTargetingGeometry geometry,
        EpicBaseTemplate template,
        EpicBaseMountPointDatabase mounts,
        EpicFactionTheme theme,
        int width,
        int height)
    {
        var origin = ToPoint(
            ResolveOrigin(
                geometry.Origin,
                template,
                mounts),
            width,
            height);

        var destinations = ResolveDestinations(
                geometry.Destination,
                template)
            .Select(point => ToPoint(point, width, height))
            .ToList();

        if (destinations.Count == 0)
        {
            throw new InvalidDataException(
                $"Targeting geometry '{geometry.Id}' has no destinations.");
        }

        var lineColour = ToColour(theme.PrimaryArcColour);
        var fillColour = ToColour(theme.ArcFillColour);

        if (IsFullForeSector(geometry))
        {
            DrawFullForeSector(
                canvas,
                origin,
                destinations,
                geometry,
                template,
                lineColour,
                fillColour,
                width,
                height);
            return;
        }

        if (geometry.GeometryType.Equals(
                "Sector",
                StringComparison.OrdinalIgnoreCase)
            || geometry.GeometryType.Equals(
                "Triangle",
                StringComparison.OrdinalIgnoreCase))
        {
            if (destinations.Count != 2)
            {
                throw new InvalidDataException(
                    $"Targeting geometry '{geometry.Id}' requires exactly two destinations.");
            }

            using var path = new SKPath();
            path.MoveTo(origin);
            path.LineTo(destinations[0]);
            path.LineTo(destinations[1]);
            path.Close();

            if (geometry.FillEnabled)
            {
                using var fill = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = fillColour
                };
                canvas.DrawPath(path, fill);
            }

            using var linePaint = ArcPaint(
                lineColour,
                geometry.Dashed);

            var firstLineDestination = destinations[0];
            if (IsRaiderAftSector(geometry))
            {
                // The fill remains calibrated to the divider centreline.
                // Only the visible green stroke is shortened so its
                // antialiased edge touches, but does not cover, the blue
                // divider stroke below the line.
                firstLineDestination = new SKPoint(
                    firstLineDestination.X,
                    firstLineDestination.Y + DividerTouchOffsetPixels);
            }

            var lineOrigin = IsDividerTouchingForeSector(geometry)
                ? new SKPoint(
                    origin.X,
                    origin.Y - DividerTouchOffsetPixels)
                : origin;

            canvas.DrawLine(
                lineOrigin,
                firstLineDestination,
                linePaint);
            canvas.DrawLine(
                lineOrigin,
                destinations[1],
                linePaint);
            return;
        }

        using var paint = ArcPaint(
            lineColour,
            geometry.Dashed);

        foreach (var destination in destinations)
            canvas.DrawLine(origin, destination, paint);
    }

    private static bool IsRaiderAftSector(
        EpicTargetingGeometry geometry) =>
        geometry.Id.Equals(
            "aft-port-sector",
            StringComparison.OrdinalIgnoreCase)
        || geometry.Id.Equals(
            "aft-starboard-sector",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDividerTouchingForeSector(
        EpicTargetingGeometry geometry) =>
        geometry.Id.Equals(
            "gozanti-fore-sector",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFullForeSector(
        EpicTargetingGeometry geometry) =>
        geometry.Id.Equals(
            "raider-fore-sector",
            StringComparison.OrdinalIgnoreCase)
        || geometry.Id.Equals(
            "gozanti-fore-sector",
            StringComparison.OrdinalIgnoreCase);

    private static void DrawFullForeSector(
        SKCanvas canvas,
        SKPoint origin,
        IReadOnlyList<SKPoint> destinations,
        EpicTargetingGeometry geometry,
        EpicBaseTemplate template,
        SKColor lineColour,
        SKColor fillColour,
        int width,
        int height)
    {
        if (destinations.Count != 2)
        {
            throw new InvalidDataException(
                "Fore targeting geometry requires exactly two shoulder destinations.");
        }

        var surface =
            template.Layout.Calibration.MainRenderedSurface;
        var forePortCorner = ToPoint(
            new EpicTokenOptionalPoint
            {
                U = surface.MinU,
                V = surface.MaxV
            },
            width,
            height);
        var foreStarboardCorner = ToPoint(
            new EpicTokenOptionalPoint
            {
                U = surface.MaxU,
                V = surface.MaxV
            },
            width,
            height);

        if (geometry.FillEnabled)
        {
            using var fillPath = new SKPath();
            fillPath.MoveTo(origin);
            fillPath.LineTo(destinations[0]);
            fillPath.LineTo(forePortCorner);
            fillPath.LineTo(foreStarboardCorner);
            fillPath.LineTo(destinations[1]);
            fillPath.Close();

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = fillColour
            };
            canvas.DrawPath(fillPath, fill);
        }

        using var linePaint = ArcPaint(
            lineColour,
            geometry.Dashed);

        var lineOrigin = new SKPoint(
            origin.X,
            origin.Y - DividerTouchOffsetPixels);

        canvas.DrawLine(
            lineOrigin,
            destinations[0],
            linePaint);
        canvas.DrawLine(
            lineOrigin,
            destinations[1],
            linePaint);
    }

    private static void DrawDivider(
        SKCanvas canvas,
        EpicBaseTemplate template,
        int width,
        int height)
    {
        var divider = template.Layout.SectionDivider;

        if (divider.Start is null || divider.End is null)
        {
            throw new InvalidDataException(
                "Divider geometry is incomplete.");
        }

        using var dark = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 9,
            Color = new SKColor(4, 34, 62, 255)
        };

        using var blue = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5,
            Color = new SKColor(25, 145, 215, 255)
        };

        var start = ToPoint(divider.Start, width, height);
        var end = ToPoint(divider.End, width, height);

        canvas.DrawLine(start, end, dark);
        canvas.DrawLine(start, end, blue);
    }

    private static void DrawTurretIndicator(
        SKCanvas canvas,
        EpicTurretIndicator indicator,
        EpicBaseMountPointDatabase mounts,
        EpicFactionTheme theme,
        int width,
        int height)
    {
        if (!indicator.Style.Equals(
                "Cr90ClockwiseAnnularArrowOutline",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unsupported turret indicator style '{indicator.Style}'.");
        }

        var centre = ToPoint(
            ResolveMount(indicator.Centre, mounts),
            width,
            height);

        DrawCr90ClockwiseAnnularArrowOutline(
            canvas,
            centre,
            width,
            ToColour(theme.PrimaryArcColour));
    }

    private static void DrawCr90ClockwiseAnnularArrowOutline(
        SKCanvas canvas,
        SKPoint centre,
        int textureWidth,
        SKColor colour)
    {
        DrawCr90TransparentTurretOverlay(
            canvas,
            centre,
            textureWidth * 0.07935f);
    }

    private static void DrawCr90TransparentTurretOverlay(
        SKCanvas canvas,
        SKPoint centre,
        float targetOuterRadius)
    {
        var repositoryRoot =
            ResolveRepositoryRootForGeneratedAsset();

        var overlayPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "cr90corvette",
            "turret_arrow.png");

        if (!File.Exists(overlayPath))
        {
            throw new FileNotFoundException(
                "The CR90 transparent turret overlay was not found.",
                overlayPath);
        }

        using var overlay = SKBitmap.Decode(overlayPath)
            ?? throw new InvalidDataException(
                "Could not decode the CR90 transparent turret overlay.");

        // Calibrated from the supplied 1800 x 1643 transparent PNG.
        // These coordinates identify the centre and outer radius of the
        // circular turret graphic inside the source canvas. The arrowhead
        // extends farther to the right and is preserved by the same scale.
        const float sourceCentreX = 824.0f;
        const float sourceCentreY = 821.5f;
        const float sourceOuterRadius = 731.5f;

        var scale = targetOuterRadius / sourceOuterRadius;

        // The correctly oriented 2026-08-08 flatbed scan of the physical FFG
        // CR90 token places the turret-ring centre approximately 1.5 scan
        // pixels left of the physical peg centre. Converted through the
        // physical-token-to-UV-surface width ratio, this is approximately
        // 1.44 px left on the 2048 x 2048 targeting texture. The R13 scan-
        // derived outer radius is approximately 162.5 px at that resolution;
        // this ratio preserves the approved centre offset independently of
        // the R13 overlay-size increase and scales proportionally with texture
        // resolution.
        var horizontalOffset = -targetOuterRadius * 0.00886f;

        var destinationLeft =
            centre.X + horizontalOffset - sourceCentreX * scale;
        var destinationTop =
            centre.Y - sourceCentreY * scale;

        var destination = new SKRect(
            destinationLeft,
            destinationTop,
            destinationLeft + overlay.Width * scale,
            destinationTop + overlay.Height * scale);

        var sampling = new SKSamplingOptions(
            SKCubicResampler.Mitchell);

        using var overlayImage = SKImage.FromBitmap(overlay);

        canvas.DrawImage(
            overlayImage,
            destination,
            sampling);
    }

    private static void DrawCr90PrintedArtwork(
        SKCanvas canvas,
        EpicBaseTemplate template,
        int textureWidth,
        int textureHeight)
    {
        var repositoryRoot =
            ResolveRepositoryRootForGeneratedAsset();

        var scanPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "cr90corvette",
            "scans",
            "CR90-physical-token-600dpi-20260808.png");

        if (!File.Exists(scanPath))
        {
            throw new FileNotFoundException(
                "The CR90 600-dpi physical-token scan was not found.",
                scanPath);
        }

        using var scan = SKBitmap.Decode(scanPath)
            ?? throw new InvalidDataException(
                "Could not decode the CR90 600-dpi physical-token scan.");

        if (scan.Width != 1912 || scan.Height != 5240)
        {
            throw new InvalidDataException(
                "The CR90 physical-token scan must be the calibrated " +
                "1912 x 5240 600-dpi source image.");
        }

        var sampling = new SKSamplingOptions(
            SKCubicResampler.Mitchell);

        // R15 retains the locked R13 targeting geometry/turret placement and
        // the R14 ship-silhouette placement. The physical First Edition scan
        // remains the authority for dashboard height and artwork placement.
        // Unlike R14, the dashboard sources span the complete physical token
        // width. A light source-resolution de-screen pass suppresses the
        // scanner/printed-cardboard dot pattern before final downsampling.
        DrawCr90DescreenedScanCrop(
            canvas,
            scan,
            template,
            new SKRectI(74, 0, 1775, 390),
            foreSection: true,
            textureWidth,
            textureHeight,
            sampling);

        DrawCr90DescreenedScanCrop(
            canvas,
            scan,
            template,
            new SKRectI(74, 4840, 1775, 5239),
            foreSection: false,
            textureWidth,
            textureHeight,
            sampling);

        // R14 colour-keyed the action artwork from the physical scan. That
        // retained the cardboard halftone and scanner colour noise. R15 uses
        // the existing First Edition ActionIcons strictly as silhouette masks,
        // then applies one clean FFG-style action green. The physical scan is
        // still authoritative for each symbol's size and position.
        DrawCr90ActionIcon(
            canvas,
            repositoryRoot,
            "target_lock.png",
            template,
            new SKRectI(1535, 776, 1650, 876),
            foreSection: true,
            textureWidth,
            textureHeight,
            sampling);

        DrawCr90ActionIcon(
            canvas,
            repositoryRoot,
            "coordinate.png",
            template,
            new SKRectI(1535, 918, 1651, 1036),
            foreSection: true,
            textureWidth,
            textureHeight,
            sampling);

        DrawCr90ActionIcon(
            canvas,
            repositoryRoot,
            "reinforce.png",
            template,
            new SKRectI(1531, 4203, 1639, 4311),
            foreSection: false,
            textureWidth,
            textureHeight,
            sampling);

        DrawCr90ActionIcon(
            canvas,
            repositoryRoot,
            "recover.png",
            template,
            new SKRectI(1523, 4355, 1642, 4457),
            foreSection: false,
            textureWidth,
            textureHeight,
            sampling);

        DrawCr90ShipSilhouette(
            canvas,
            repositoryRoot,
            template,
            textureWidth,
            textureHeight,
            sampling);
    }

    private static void DrawCr90DescreenedScanCrop(
        SKCanvas canvas,
        SKBitmap scan,
        EpicBaseTemplate template,
        SKRectI source,
        bool foreSection,
        int textureWidth,
        int textureHeight,
        SKSamplingOptions sampling)
    {
        using var cleaned = new SKBitmap(
            source.Width,
            source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        cleaned.Erase(SKColors.Transparent);

        using var descreenFilter = SKImageFilter.CreateBlur(1.15f, 1.15f);
        using (var cleanCanvas = new SKCanvas(cleaned))
        using (var scanImage = SKImage.FromBitmap(scan))
        using (var cleanPaint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = descreenFilter
        })
        {
            cleanCanvas.DrawImage(
                scanImage,
                new SKRect(
                    source.Left,
                    source.Top,
                    source.Right,
                    source.Bottom),
                new SKRect(
                    0,
                    0,
                    source.Width,
                    source.Height),
                sampling,
                cleanPaint);
        }

        using var cleanedImage = SKImage.FromBitmap(cleaned);
        var destination = MapCr90ScanRect(
            source,
            foreSection,
            template,
            textureWidth,
            textureHeight);

        canvas.DrawImage(
            cleanedImage,
            destination,
            sampling);
    }

    private static void DrawCr90ActionIcon(
        SKCanvas canvas,
        string repositoryRoot,
        string iconFileName,
        EpicBaseTemplate template,
        SKRectI physicalVisibleBounds,
        bool foreSection,
        int textureWidth,
        int textureHeight,
        SKSamplingOptions sampling)
    {
        var iconPath = Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "ActionIcons",
            iconFileName);

        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException(
                $"The First Edition action-icon reference '{iconFileName}' was not found.",
                iconPath);
        }

        using var sourceIcon = SKBitmap.Decode(iconPath)
            ?? throw new InvalidDataException(
                $"Could not decode action-icon reference '{iconFileName}'.");

        using var cleanIcon = new SKBitmap(
            sourceIcon.Width,
            sourceIcon.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        cleanIcon.Erase(SKColors.Transparent);

        // Solid colour intentionally replaces the colour information in the
        // small reference PNG. Only its alpha silhouette is retained.
        var actionGreen = new SKColor(119, 160, 45, 255);

        for (var y = 0; y < sourceIcon.Height; y++)
        {
            for (var x = 0; x < sourceIcon.Width; x++)
            {
                var alpha = sourceIcon.GetPixel(x, y).Alpha;
                if (alpha == 0)
                    continue;

                cleanIcon.SetPixel(
                    x,
                    y,
                    actionGreen.WithAlpha(alpha));
            }
        }

        using var cleanImage = SKImage.FromBitmap(cleanIcon);
        var destination = MapCr90ScanRect(
            physicalVisibleBounds,
            foreSection,
            template,
            textureWidth,
            textureHeight);

        canvas.DrawImage(
            cleanImage,
            destination,
            sampling);
    }

    private static void DrawCr90ShipSilhouette(
        SKCanvas canvas,
        string repositoryRoot,
        EpicBaseTemplate template,
        int textureWidth,
        int textureHeight,
        SKSamplingOptions sampling)
    {
        var iconPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "ships",
            "epic",
            "cr90corvette",
            "icon.png");

        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException(
                "The CR90 transparent ship silhouette was not found.",
                iconPath);
        }

        using var icon = SKBitmap.Decode(iconPath)
            ?? throw new InvalidDataException(
                "Could not decode the CR90 transparent ship silhouette.");

        if (icon.Width != 94 || icon.Height != 94)
        {
            throw new InvalidDataException(
                "The calibrated CR90 ship silhouette must be 94 x 94 pixels.");
        }

        // The non-transparent content of the existing 94 x 94 icon occupies
        // x=4..91 and y=16..83. Its physical printed bounds were measured from
        // the 600-dpi scan at x=115..578 and y=4121..4479.
        var visibleDestination = MapCr90ScanRect(
            new SKRectI(115, 4121, 579, 4480),
            foreSection: false,
            template,
            textureWidth,
            textureHeight);

        var scaleX = visibleDestination.Width / 88.0f;
        var scaleY = visibleDestination.Height / 68.0f;
        var destination = new SKRect(
            visibleDestination.Left - 4.0f * scaleX,
            visibleDestination.Top - 16.0f * scaleY,
            visibleDestination.Left - 4.0f * scaleX
                + icon.Width * scaleX,
            visibleDestination.Top - 16.0f * scaleY
                + icon.Height * scaleY);

        using var iconImage = SKImage.FromBitmap(icon);
        canvas.DrawImage(
            iconImage,
            destination,
            sampling);
    }

    private static SKRect MapCr90ScanRect(
        SKRectI source,
        bool foreSection,
        EpicBaseTemplate template,
        int textureWidth,
        int textureHeight)
    {
        // Full-resolution Canon LiDE 400 scan calibration. The source token
        // surface spans x=74..1775, the Fore/Aft divider is centred at y=2615,
        // and the Aft physical edge is y=5239. X maps across the common UV
        // surface; each physical section maps independently to its matching
        // UV section so the printed Fore/Aft boundaries remain authoritative.
        const float scanSurfaceLeft = 74.0f;
        const float scanSurfaceRight = 1775.0f;
        const float scanDividerY = 2615.0f;
        const float scanBottomY = 5239.0f;

        var surface = ToRect(
            template.Layout.Calibration.MainRenderedSurface,
            textureWidth,
            textureHeight);
        var dividerY = ToPoint(
            template.Layout.SectionDivider.Start!,
            textureWidth,
            textureHeight).Y;

        float MapX(float x) =>
            surface.Left
            + (x - scanSurfaceLeft)
            / (scanSurfaceRight - scanSurfaceLeft)
            * surface.Width;

        float MapY(float y)
        {
            if (foreSection)
            {
                return surface.Top
                    + y / scanDividerY
                    * (dividerY - surface.Top);
            }

            return dividerY
                + (y - scanDividerY)
                / (scanBottomY - scanDividerY)
                * (surface.Bottom - dividerY);
        }

        return new SKRect(
            MapX(source.Left),
            MapY(source.Top),
            MapX(source.Right),
            MapY(source.Bottom));
    }

    private static string ResolveRepositoryRootForGeneratedAsset()
    {
        var current = new DirectoryInfo(
            Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (Directory.Exists(
                    Path.Combine(current.FullName, "assets"))
                && Directory.Exists(
                    Path.Combine(current.FullName, "tools")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not resolve the repository root from the current directory.");
    }

    private static SKPoint PointOnCircle(
        SKPoint centre,
        float radius,
        float degrees)
    {
        var radians = degrees * Math.PI / 180.0;

        return new SKPoint(
            centre.X + radius * (float)Math.Cos(radians),
            centre.Y + radius * (float)Math.Sin(radians));
    }

    private static EpicTokenOptionalPoint ResolveOrigin(
        string name,
        EpicBaseTemplate template,
        EpicBaseMountPointDatabase mounts) =>
        name switch
        {
            "ForeMount" => ResolveMount(name, mounts),
            "AftMount" => ResolveMount(name, mounts),
            "BaseCentre" => BaseCentre(template),
            "DividerCentre" => DividerCentre(template),
            _ => throw new InvalidDataException(
                $"Unknown targeting origin '{name}'.")
        };

    private static EpicTokenOptionalPoint DividerCentre(
        EpicBaseTemplate template)
    {
        var divider = template.Layout.SectionDivider;

        if (divider.Start is null || divider.End is null)
        {
            throw new InvalidDataException(
                "Divider geometry is incomplete.");
        }

        return new EpicTokenOptionalPoint
        {
            U = (divider.Start.U + divider.End.U) / 2.0,
            V = (divider.Start.V + divider.End.V) / 2.0
        };
    }

    private static EpicTokenOptionalPoint BaseCentre(
        EpicBaseTemplate template)
    {
        var surface =
            template.Layout.Calibration.MainRenderedSurface;

        return new EpicTokenOptionalPoint
        {
            U = (surface.MinU + surface.MaxU) / 2.0,
            V = (surface.MinV + surface.MaxV) / 2.0
        };
    }

    private static EpicTokenOptionalPoint ResolveMount(
        string name,
        EpicBaseMountPointDatabase mounts)
    {
        var id = name.StartsWith(
            "Fore",
            StringComparison.OrdinalIgnoreCase)
            ? "fore"
            : "aft";

        return mounts.MountPoints.Single(
            point => point.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase))
            .TextureUv;
    }

    private static List<EpicTokenOptionalPoint> ResolveDestinations(
        string name,
        EpicBaseTemplate template)
    {
        var surface =
            template.Layout.Calibration.MainRenderedSurface;

        var dividerV =
            template.Layout.SectionDivider.Start?.V
            ?? throw new InvalidDataException(
                "Divider coordinate is missing.");

        EpicTokenOptionalPoint P(
            double u,
            double v) =>
            new()
            {
                U = u,
                V = v
            };

        return name switch
        {
            "ForeOuterCorners" => new List<EpicTokenOptionalPoint>
            {
                P(surface.MinU, surface.MaxV),
                P(surface.MaxU, surface.MaxV)
            },

            "RaiderForeShoulderCorners" =>
                ResolveRaiderForeShoulderCorners(template),

            "GozantiForeShoulderCorners" =>
                ResolveGozantiForeShoulderCorners(template),

            "RaiderAftPortCorners" => new List<EpicTokenOptionalPoint>
            {
                P(surface.MinU, dividerV),
                P(surface.MinU, surface.MinV)
            },

            "RaiderAftStarboardCorners" => new List<EpicTokenOptionalPoint>
            {
                P(surface.MaxU, dividerV),
                P(surface.MaxU, surface.MinV)
            },

            "ForePortSectionCorners" =>
                ResolveCr90SquareSideCorners(
                    template,
                    "fore",
                    "port"),

            "ForeStarboardSectionCorners" =>
                ResolveCr90SquareSideCorners(
                    template,
                    "fore",
                    "starboard"),

            "AftPortSectionCorners" =>
                ResolveCr90SquareSideCorners(
                    template,
                    "aft",
                    "port"),

            "AftStarboardSectionCorners" =>
                ResolveCr90SquareSideCorners(
                    template,
                    "aft",
                    "starboard"),

            _ => throw new InvalidDataException(
                $"Unknown targeting destination '{name}'.")
        };
    }

    private static List<EpicTokenOptionalPoint>
        ResolveRaiderForeShoulderCorners(
            EpicBaseTemplate template)
    {
        var surface =
            template.Layout.Calibration.MainRenderedSurface;

        // The 600 DPI First Edition Raider scan places the two Fore-sector
        // endpoints exactly at the long Epic base's Fore/centre shoulder.
        // In the authoritative base.obj top-surface UV island, the shoulder
        // is the z=-1.783 transition and has V=0.640178.
        const double foreShoulderV = 0.640178;

        return new List<EpicTokenOptionalPoint>
        {
            new()
            {
                U = surface.MinU,
                V = foreShoulderV
            },
            new()
            {
                U = surface.MaxU,
                V = foreShoulderV
            }
        };
    }

    private static List<EpicTokenOptionalPoint>
        ResolveGozantiForeShoulderCorners(
            EpicBaseTemplate template)
    {
        var surface =
            template.Layout.Calibration.MainRenderedSurface;

        // The supplied 1880 x 4544 600-dpi First Edition Gozanti scan places
        // both green Fore-sector endpoints on the Fore/centre shoulder. The
        // short Epic mesh removes centre length without changing this end-
        // section UV landmark, so it retains V=0.640178.
        const double foreShoulderV = 0.640178;

        return new List<EpicTokenOptionalPoint>
        {
            new()
            {
                U = surface.MinU,
                V = foreShoulderV
            },
            new()
            {
                U = surface.MaxU,
                V = foreShoulderV
            }
        };
    }

    private static List<EpicTokenOptionalPoint>
        ResolveCr90SquareSideCorners(
            EpicBaseTemplate template,
            string sectionId,
            string sideId)
    {
        var surface =
            template.Layout.Calibration.MainRenderedSurface;

        var markerId = sectionId.Equals(
            "fore",
            StringComparison.OrdinalIgnoreCase)
            ? "fore-mount-marker"
            : "aft-mount-marker";

        var marker = template.Layout.ShipMountMarkers.Single(
            item => item.Id.Equals(
                markerId,
                StringComparison.OrdinalIgnoreCase));

        if (marker.Centre is null)
        {
            throw new InvalidDataException(
                $"Mount marker '{markerId}' has no calibrated centre.");
        }

        // The textured width is the side length of each physical
        // large-base square. Each mount is the square centre.
        var squareSideUv =
            surface.MaxU - surface.MinU;
        var halfSquareUv =
            squareSideUv / 2.0;

        var squareMinV = Math.Max(
            surface.MinV,
            marker.Centre.V - halfSquareUv);
        var squareMaxV = Math.Min(
            surface.MaxV,
            marker.Centre.V + halfSquareUv);

        var sideU = sideId.Equals(
            "port",
            StringComparison.OrdinalIgnoreCase)
            ? surface.MinU
            : surface.MaxU;

        return new List<EpicTokenOptionalPoint>
        {
            new()
            {
                U = sideU,
                V = squareMaxV
            },
            new()
            {
                U = sideU,
                V = squareMinV
            }
        };
    }

    private static SKPaint ArcPaint(
        SKColor colour,
        bool dashed)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = colour
        };

        if (dashed)
        {
            paint.PathEffect = SKPathEffect.CreateDash(
                new float[] { 18, 12 },
                0);
        }

        return paint;
    }

    private static T Deserialize<T>(
        string path) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(path),
            JsonOptions)
        ?? throw new InvalidDataException(
            $"Could not deserialize '{path}'.");

    private static SKPoint ToPoint(
        EpicTokenOptionalPoint point,
        int width,
        int height) =>
        new(
            (float)(point.U * width),
            (float)((1.0 - point.V) * height));

    private static SKRect ToRect(
        EpicTokenUvBounds bounds,
        int width,
        int height) =>
        new(
            (float)(bounds.MinU * width),
            (float)((1.0 - bounds.MaxV) * height),
            (float)(bounds.MaxU * width),
            (float)((1.0 - bounds.MinV) * height));

    private static SKColor ToColour(
        EpicThemeColour colour) =>
        new(
            colour.R,
            colour.G,
            colour.B,
            colour.A);

    private static void WriteReport(
        string repositoryRoot,
        EpicShipTargetingLayout layout,
        EpicShipTargetingTextureResult result)
    {
        var lines = new List<string>
        {
            $"# {layout.ShipName} Targeting Diagnostic — Phase 15C-R19",
            "",
            $"- Ship: {layout.ShipId}",
            $"- Faction: {layout.FactionId}",
            $"- Divider visible: {layout.Divider.Visible}",
            $"- Targeting geometry: {result.GeometryCount}",
            $"- Turret indicators: {result.TurretIndicatorCount}",
            $"- Reference: `{layout.ReferenceImage}`",
            $"- Output: `{Relative(repositoryRoot, result.OutputPath)}`",
            $"- SHA-256: `{result.Sha256}`",
            "",
            "## Layout notes",
            ""
        };

        lines.AddRange(layout.Notes.Select(note => $"- {note}"));

        lines.Add("");
        lines.Add(
            layout.ShipId.Equals(
                "gozanticlasscruiser",
                StringComparison.OrdinalIgnoreCase)
                ? "The supplied 1880 x 4544 Canon LiDE 400 600-dpi First Edition Gozanti scan is the geometry authority. The lower-resolution photograph is used only as a visual cross-check."
                : layout.ShipId.Equals(
                    "cr90corvette",
                    StringComparison.OrdinalIgnoreCase)
                ? "The 1912 x 5240 Canon LiDE 400 scan remains the CR90 measurement authority. Unified 2.5 CR90 artwork is not used."
                : "The registered First Edition physical-token reference remains the geometry authority.");

        File.WriteAllLines(
            result.ReportPath,
            lines,
            new UTF8Encoding(false));
    }

    private static string Relative(
        string repositoryRoot,
        string path) =>
        Path.GetRelativePath(repositoryRoot, path)
            .Replace('\\', '/');

    private static void ValidateFile(
        string path,
        string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{description} was not found.",
                path);
        }
    }
}
