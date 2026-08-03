using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicCommonArtworkTextureGenerator
{
    private const int StarSeed = 150315;
    private const int StarCount = 720;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static EpicCommonArtworkTextureResult Generate(
        string repositoryRoot,
        string? templatePath = null,
        string? mountPointDatabasePath = null,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);

        templatePath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-template.json");
        templatePath = Path.GetFullPath(templatePath);

        mountPointDatabasePath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-mount-points.json");
        mountPointDatabasePath = Path.GetFullPath(
            mountPointDatabasePath);

        ValidateFile(templatePath, "Epic base template");
        ValidateFile(
            mountPointDatabasePath,
            "Epic base mount-point database");

        var template = JsonSerializer.Deserialize<EpicBaseTemplate>(
            File.ReadAllText(templatePath),
            JsonOptions)
            ?? throw new InvalidDataException(
                "Could not deserialize the Epic base template.");

        var mountPoints =
            JsonSerializer.Deserialize<EpicBaseMountPointDatabase>(
                File.ReadAllText(mountPointDatabasePath),
                JsonOptions)
            ?? throw new InvalidDataException(
                "Could not deserialize the Epic mount-point database.");

        ValidateInputs(template, mountPoints);

        outputPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "epic",
            "common",
            "epic-common-artwork-r2.png");
        outputPath = Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException(
                "Epic artwork output has no parent directory."));

        using var bitmap = new SKBitmap(
            template.Canvas.Width,
            template.Canvas.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        var surface = template.Layout.Calibration.MainRenderedSurface;
        var surfaceRect = ToRect(
            surface,
            bitmap.Width,
            bitmap.Height);

        canvas.Save();
        canvas.ClipRect(surfaceRect);

        DrawBackground(canvas, surfaceRect);
        DrawStars(canvas, surfaceRect);
        DrawSectionTint(canvas, template, bitmap.Width, bitmap.Height);
        DrawDivider(canvas, template, bitmap.Width, bitmap.Height);
        DrawMountGuides(canvas, template, mountPoints, bitmap.Width, bitmap.Height);

        canvas.Restore();

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Png,
            100);
        using (var output = File.Create(outputPath))
            encoded.SaveTo(output);

        using var hashStream = File.OpenRead(outputPath);
        var sha256 = Convert.ToHexString(
            SHA256.HashData(hashStream));

        var reportPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase15",
            "epic-artwork",
            "EPIC-COMMON-ARTWORK-R2.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        var result = new EpicCommonArtworkTextureResult
        {
            TemplatePath = templatePath,
            MountPointDatabasePath = mountPointDatabasePath,
            OutputPath = outputPath,
            ReportPath = reportPath,
            Width = bitmap.Width,
            Height = bitmap.Height,
            StarCount = StarCount,
            Sha256 = sha256
        };

        WriteReport(repositoryRoot, result);
        return result;
    }

    private static void DrawBackground(
        SKCanvas canvas,
        SKRect surface)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(surface.MidX, surface.Top),
            new SKPoint(surface.MidX, surface.Bottom),
            new[]
            {
                new SKColor(5, 7, 11),
                new SKColor(12, 8, 12),
                new SKColor(5, 7, 11)
            },
            null,
            SKShaderTileMode.Clamp);

        paint.Shader = shader;
        canvas.DrawRect(surface, paint);
    }

    private static void DrawStars(
        SKCanvas canvas,
        SKRect surface)
    {
        var random = new Random(StarSeed);

        for (var index = 0; index < StarCount; index++)
        {
            var x = surface.Left
                + (float)random.NextDouble() * surface.Width;
            var y = surface.Top
                + (float)random.NextDouble() * surface.Height;

            var radiusChoice = random.NextDouble();
            var radius = radiusChoice < 0.88
                ? 1.0f
                : radiusChoice < 0.98
                    ? 1.7f
                    : 2.5f;

            var alpha = (byte)random.Next(90, 235);

            using var starPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(235, 240, 245, alpha)
            };

            canvas.DrawCircle(x, y, radius, starPaint);
        }
    }

    private static void DrawSectionTint(
        SKCanvas canvas,
        EpicBaseTemplate template,
        int width,
        int height)
    {
        var fore = RequiredSection(template, "fore");
        var aft = RequiredSection(template, "aft");

        using var forePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(75, 0, 12, 32)
        };
        using var aftPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(75, 0, 12, 24)
        };

        canvas.DrawRect(
            ToRect(fore, width, height),
            forePaint);
        canvas.DrawRect(
            ToRect(aft, width, height),
            aftPaint);
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
                "The Epic base divider is incomplete.");
        }

        using var outer = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 11,
            Color = new SKColor(12, 16, 22, 255)
        };
        using var inner = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5,
            Color = new SKColor(180, 185, 195, 255)
        };

        var startX = ToX(divider.Start.U, width);
        var startY = ToY(divider.Start.V, height);
        var endX = ToX(divider.End.U, width);
        var endY = ToY(divider.End.V, height);

        canvas.DrawLine(startX, startY, endX, endY, outer);
        canvas.DrawLine(startX, startY, endX, endY, inner);
    }

    private static void DrawMountGuides(
        SKCanvas canvas,
        EpicBaseTemplate template,
        EpicBaseMountPointDatabase mountPoints,
        int width,
        int height)
    {
        var templateMarkers = template.Layout.ShipMountMarkers
            .ToDictionary(
                marker => marker.Id,
                StringComparer.OrdinalIgnoreCase);

        foreach (var mountPoint in mountPoints.MountPoints)
        {
            var markerId = mountPoint.Id.Equals(
                "fore",
                StringComparison.OrdinalIgnoreCase)
                ? "fore-mount-marker"
                : "aft-mount-marker";

            if (!templateMarkers.TryGetValue(
                    markerId,
                    out var marker)
                || marker.GuideCircleRadius is null)
            {
                throw new InvalidDataException(
                    $"The template marker '{markerId}' is incomplete.");
            }

            var x = ToX(mountPoint.TextureUv.U, width);
            var y = ToY(mountPoint.TextureUv.V, height);
            var radius = (float)(
                marker.GuideCircleRadius.Value * width);

            using var dark = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8,
                Color = new SKColor(10, 12, 16, 245)
            };
            dark.PathEffect = SKPathEffect.CreateDash(
                new float[] { 22, 15 },
                0);

            using var light = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4,
                Color = new SKColor(185, 190, 195, 255)
            };
            light.PathEffect = SKPathEffect.CreateDash(
                new float[] { 22, 15 },
                0);

            canvas.DrawCircle(x, y, radius, dark);
            canvas.DrawCircle(x, y, radius, light);
        }
    }

    private static EpicTokenOptionalBounds RequiredSection(
        EpicBaseTemplate template,
        string id) =>
        template.Layout.Sections
            .Single(section =>
                section.Id.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase))
            .Bounds
        ?? throw new InvalidDataException(
            $"The Epic template section '{id}' has no bounds.");

    private static EpicBaseMountPoint RequiredMountPoint(
        EpicBaseMountPointDatabase database,
        string id) =>
        database.MountPoints.Single(point =>
            point.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));

    private static void ValidateInputs(
        EpicBaseTemplate template,
        EpicBaseMountPointDatabase mountPoints)
    {
        if (!template.Layout.Status.Equals(
                "CommonEpicBaseTemplateCalibrated",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Epic base template is not calibrated.");
        }

        if (mountPoints.MountPoints.Count != 2)
        {
            throw new InvalidDataException(
                "Exactly two Epic mount points are required.");
        }

        if (!mountPoints.Projection.Method.Contains(
                "PegShaftAxis",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The mount-point database must use the approved " +
                "peg-shaft-axis projection.");
        }
    }

    private static void WriteReport(
        string repositoryRoot,
        EpicCommonArtworkTextureResult result)
    {
        var lines = new List<string>
        {
            "# Epic Common Artwork — Phase 15C-R2",
            "",
            "## Generated layers",
            "",
            "- Deterministic dark starfield",
            "- Shared Fore/Aft section tint",
            "- Shared Fore/Aft divider",
            "- Printed mount-guide circles at validated peg centres",
            "",
            "## Deliberately not generated",
            "",
            "- Ship icon",
            "- Ship-specific firing arcs",
            "- Ship title",
            "- Statistics",
            "- Action symbols",
            "- Ship-specific decorative panels",
            "",
            $"Canvas: {result.Width} × {result.Height}",
            $"Stars: {result.StarCount}",
            $"SHA-256: `{result.Sha256}`",
            $"Template: `{Relative(repositoryRoot, result.TemplatePath)}`",
            $"Mount points: `{Relative(repositoryRoot, result.MountPointDatabasePath)}`",
            $"Output: `{Relative(repositoryRoot, result.OutputPath)}`",
            "",
            "This reusable common texture contains no firing arcs or ship-specific content."
        };

        File.WriteAllLines(
            result.ReportPath,
            lines,
            new UTF8Encoding(false));
    }

    private static SKRect ToRect(
        EpicTokenUvBounds bounds,
        int width,
        int height) =>
        new(
            ToX(bounds.MinU, width),
            ToY(bounds.MaxV, height),
            ToX(bounds.MaxU, width),
            ToY(bounds.MinV, height));

    private static SKRect ToRect(
        EpicTokenOptionalBounds bounds,
        int width,
        int height) =>
        new(
            ToX(bounds.MinU, width),
            ToY(bounds.MaxV, height),
            ToX(bounds.MaxU, width),
            ToY(bounds.MinV, height));

    private static float ToX(
        double u,
        int width) =>
        (float)(u * width);

    private static float ToY(
        double v,
        int height) =>
        (float)((1.0 - v) * height);

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
