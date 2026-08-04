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
            $"{layout.ShipId}-targeting-r4.png");

        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

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
            $"{layout.ShipId.ToUpperInvariant()}-TARGETING-R4.md");

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
                "Cr90DualRotationArrows",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unsupported turret indicator style '{indicator.Style}'.");
        }

        var centre = ToPoint(
            ResolveMount(indicator.Centre, mounts),
            width,
            height);

        var colour = ToColour(theme.PrimaryArcColour);

        DrawClockwiseRotationArrow(
            canvas,
            centre,
            width * 0.058f,
            30,
            282,
            colour,
            5.5f,
            width * 0.019f);

        DrawClockwiseRotationArrow(
            canvas,
            centre,
            width * 0.043f,
            205,
            292,
            colour,
            5.0f,
            width * 0.016f);
    }

    private static void DrawClockwiseRotationArrow(
        SKCanvas canvas,
        SKPoint centre,
        float radius,
        float startDegrees,
        float sweepDegrees,
        SKColor colour,
        float strokeWidth,
        float arrowLength)
    {
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Miter,
            Color = colour
        };

        var oval = new SKRect(
            centre.X - radius,
            centre.Y - radius,
            centre.X + radius,
            centre.Y + radius);

        canvas.DrawArc(
            oval,
            startDegrees,
            sweepDegrees,
            false,
            stroke);

        var endDegrees = startDegrees + sweepDegrees;
        var angle = endDegrees * Math.PI / 180.0;
        var tangent = (endDegrees + 90.0) * Math.PI / 180.0;

        var tip = new SKPoint(
            centre.X + radius * (float)Math.Cos(angle),
            centre.Y + radius * (float)Math.Sin(angle));

        var back = new SKPoint(
            tip.X - arrowLength * (float)Math.Cos(tangent),
            tip.Y - arrowLength * (float)Math.Sin(tangent));

        var halfWidth = arrowLength * 0.46f;
        var normal = tangent + Math.PI / 2.0;

        var left = new SKPoint(
            back.X + halfWidth * (float)Math.Cos(normal),
            back.Y + halfWidth * (float)Math.Sin(normal));

        var right = new SKPoint(
            back.X - halfWidth * (float)Math.Cos(normal),
            back.Y - halfWidth * (float)Math.Sin(normal));

        using var arrow = new SKPath();
        arrow.MoveTo(tip);
        arrow.LineTo(left);
        arrow.LineTo(right);
        arrow.Close();

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = colour
        };

        canvas.DrawPath(arrow, fill);
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
                new List<EpicTokenOptionalPoint>
                {
                    P(surface.MinU, surface.MaxV),
                    P(surface.MinU, dividerV)
                },

            "ForeStarboardSectionCorners" =>
                new List<EpicTokenOptionalPoint>
                {
                    P(surface.MaxU, surface.MaxV),
                    P(surface.MaxU, dividerV)
                },

            "AftPortSectionCorners" =>
                new List<EpicTokenOptionalPoint>
                {
                    P(surface.MinU, dividerV),
                    P(surface.MinU, surface.MinV)
                },

            "AftStarboardSectionCorners" =>
                new List<EpicTokenOptionalPoint>
                {
                    P(surface.MaxU, dividerV),
                    P(surface.MaxU, surface.MinV)
                },

            _ => throw new InvalidDataException(
                $"Unknown targeting destination '{name}'.")
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
            $"# {layout.ShipName} Targeting Diagnostic — Phase 15C-R4",
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
            "No ship icon, title, statistics, actions or dashboard artwork was generated."
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
