using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicShipTargetingTextureGenerator
{
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
            $"{layout.ShipId}-targeting-r13.png");

        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (File.Exists(outputPath))
        {
            throw new IOException(
                $"The R13 output already exists and will not be overwritten: {outputPath}");
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
            $"{layout.ShipId.ToUpperInvariant()}-TARGETING-R13.md");

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

            canvas.DrawLine(
                origin,
                destinations[0],
                linePaint);
            canvas.DrawLine(
                origin,
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
            _ => throw new InvalidDataException(
                $"Unknown targeting origin '{name}'.")
        };

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
            $"# {layout.ShipName} Targeting Diagnostic — Phase 15C-R13",
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
            "R13 retains the approved CR90 firing arcs and composites the user-supplied transparent turret_arrow.png over the calibrated Fore mount. The overlay uses the correctly oriented 2026-08-08 physical FFG scan to increase the R12 outer radius from approximately 124.9 px to 162.5 px while retaining the scan-derived 1.44 px left centre offset and unchanged vertical placement. No photographic extraction or procedural turret construction is used. No icon, title, statistics, actions or dashboard artwork was generated."
        };

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
