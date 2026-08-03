using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicBaseCalibrationTextureGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static EpicBaseCalibrationTextureResult Generate(
        string repositoryRoot,
        string? templatePath = null,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        templatePath = templatePath is null
            ? Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified1e",
                "reference",
                "epic",
                "epic-base-template.json")
            : Path.GetFullPath(templatePath);

        ValidateFile(templatePath, "Epic base template");

        var template = JsonSerializer.Deserialize<EpicBaseTemplate>(
            File.ReadAllText(templatePath),
            JsonOptions)
            ?? throw new InvalidDataException(
                "Could not deserialize the Epic base template.");

        if (!template.Layout.Status.Equals(
                "CommonEpicBaseTemplateCalibrated",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Epic base template must be calibrated before " +
                "a calibration texture can be generated.");
        }

        outputPath = outputPath is null
            ? Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "epic",
                "calibration",
                "epic-base-calibration.png")
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException(
                "Calibration texture output has no parent directory."));

        using var bitmap = new SKBitmap(
            template.Canvas.Width,
            template.Canvas.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(12, 14, 20, 255));

        DrawGrid(canvas, bitmap.Width, bitmap.Height);

        var surface = template.Layout.Calibration.MainRenderedSurface;
        var surfaceRect = ToRect(
            surface,
            bitmap.Width,
            bitmap.Height);

        using var surfaceFill = Fill(
            new SKColor(18, 26, 38, 255));
        using var surfaceOutline = Stroke(
            new SKColor(0, 255, 255, 255),
            5);
        canvas.DrawRect(surfaceRect, surfaceFill);
        canvas.DrawRect(surfaceRect, surfaceOutline);

        var fore = template.Layout.Sections.Single(
            section => section.Id == "fore").Bounds
            ?? throw new InvalidDataException(
                "Fore section bounds are missing.");
        var aft = template.Layout.Sections.Single(
            section => section.Id == "aft").Bounds
            ?? throw new InvalidDataException(
                "Aft section bounds are missing.");

        using var foreFill = Fill(
            new SKColor(0, 150, 255, 34));
        using var aftFill = Fill(
            new SKColor(190, 0, 255, 30));
        canvas.DrawRect(ToRect(fore, bitmap.Width, bitmap.Height), foreFill);
        canvas.DrawRect(ToRect(aft, bitmap.Width, bitmap.Height), aftFill);

        var divider = template.Layout.SectionDivider;
        if (divider.Start is null || divider.End is null)
        {
            throw new InvalidDataException(
                "The calibrated section divider is incomplete.");
        }

        using var dividerPaint = Stroke(
            new SKColor(0, 120, 255, 255),
            8);
        canvas.DrawLine(
            ToX(divider.Start.U, bitmap.Width),
            ToY(divider.Start.V, bitmap.Height),
            ToX(divider.End.U, bitmap.Width),
            ToY(divider.End.V, bitmap.Height),
            dividerPaint);

        foreach (var marker in template.Layout.ShipMountMarkers)
        {
            if (marker.Centre is null
                || marker.GuideCircleRadius is null)
            {
                throw new InvalidDataException(
                    $"Ship mount marker '{marker.Id}' is incomplete.");
            }

            var x = ToX(marker.Centre.U, bitmap.Width);
            var y = ToY(marker.Centre.V, bitmap.Height);
            var radius = (float)(
                marker.GuideCircleRadius.Value
                * bitmap.Width);

            using var markerPaint = Stroke(
                new SKColor(255, 220, 0, 255),
                6);
            markerPaint.PathEffect = SKPathEffect.CreateDash(
                new float[] { 22, 16 },
                0);
            using var crossPaint = Stroke(
                new SKColor(255, 220, 0, 255),
                5);

            canvas.DrawCircle(x, y, radius, markerPaint);
            canvas.DrawLine(x - 28, y, x + 28, y, crossPaint);
            canvas.DrawLine(x, y - 28, x, y + 28, crossPaint);
        }

        DrawOrientationLabels(
            canvas,
            template,
            bitmap.Width,
            bitmap.Height);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);
        using (var stream = File.Create(outputPath))
            data.SaveTo(stream);

        using var hashStream = File.OpenRead(outputPath);
        var sha256 = Convert.ToHexString(
            SHA256.HashData(hashStream));

        return new EpicBaseCalibrationTextureResult
        {
            TemplatePath = templatePath,
            OutputPath = outputPath,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Sha256 = sha256
        };
    }

    private static void DrawGrid(
        SKCanvas canvas,
        int width,
        int height)
    {
        using var minor = Stroke(
            new SKColor(255, 255, 255, 22),
            1);
        using var major = Stroke(
            new SKColor(255, 255, 255, 50),
            2);

        const int minorStep = 64;
        const int majorStep = 256;

        for (var x = 0; x <= width; x += minorStep)
        {
            canvas.DrawLine(
                x,
                0,
                x,
                height,
                x % majorStep == 0 ? major : minor);
        }

        for (var y = 0; y <= height; y += minorStep)
        {
            canvas.DrawLine(
                0,
                y,
                width,
                y,
                y % majorStep == 0 ? major : minor);
        }
    }

    private static void DrawOrientationLabels(
        SKCanvas canvas,
        EpicBaseTemplate template,
        int width,
        int height)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White
        };
        using var font = new SKFont(
            SKTypeface.Default,
            52);

        var fore = template.Layout.Sections.Single(
            section => section.Id == "fore").Bounds!;
        var aft = template.Layout.Sections.Single(
            section => section.Id == "aft").Bounds!;

        canvas.DrawText(
            "FORE",
            (ToX(fore.MinU, width) + ToX(fore.MaxU, width)) / 2,
            ToY(fore.MaxV, height) + 72,
            SKTextAlign.Center,
            font,
            paint);
        canvas.DrawText(
            "AFT",
            (ToX(aft.MinU, width) + ToX(aft.MaxU, width)) / 2,
            ToY(aft.MinV, height) - 28,
            SKTextAlign.Center,
            font,
            paint);
    }

    private static SKPaint Stroke(
        SKColor colour,
        float width) =>
        new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            Color = colour
        };

    private static SKPaint Fill(
        SKColor colour) =>
        new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = colour
        };

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
