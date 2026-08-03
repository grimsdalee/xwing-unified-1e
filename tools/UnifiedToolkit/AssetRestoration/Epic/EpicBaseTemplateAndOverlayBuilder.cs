using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicBaseTemplateBuilder
{
    // Main rendered top-surface UV island from base.obj.
    private const double SurfaceMinU = 0.035272;
    private const double SurfaceMinV = 0.020282;
    private const double SurfaceMaxU = 0.343581;
    private const double SurfaceMaxV = 0.991312;

    // Normalized physical-component measurements from full.jpg. These values
    // describe common base geometry only; they do not copy CR90 artwork.
    private const double DividerPhotoY = 0.4950;
    private const double ForeMountPhotoX = 0.4830;
    private const double ForeMountPhotoY = 0.1570;
    private const double AftMountPhotoX = 0.5200;
    private const double AftMountPhotoY = 0.8040;
    private const double MountGuideRadiusSurfaceWidth = 0.1100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EpicBaseTemplateBuildResult Build(
        string repositoryRoot,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);

        // CR90 is used only to validate the shared Epic mesh/template inputs.
        // No CR90-specific icon, photograph, or artwork region is copied into
        // the common template.
        var source = EpicTokenBlueprintBuilder.Analyse(
            repositoryRoot,
            "cr90corvette");

        var template = new EpicBaseTemplate
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Canvas = source.Canvas,
            Mesh = source.Mesh,
            Template = source.Template,
            ValidationWarnings = source.ValidationWarnings.ToList(),
            Layout = BuildCalibratedLayout(
                repositoryRoot,
                source)
        };

        var defaultPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-template.json");
        var templatePath = outputPath is null
            ? defaultPath
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(templatePath)
            ?? throw new InvalidDataException(
                "Epic base template output has no parent directory."));

        File.WriteAllText(
            templatePath,
            JsonSerializer.Serialize(template, JsonOptions),
            new UTF8Encoding(false));

        var calibrationOverlayPath = Path.Combine(
            Path.GetDirectoryName(templatePath)!,
            "epic-base-template-calibration-overlay.png");
        WriteCalibrationOverlay(
            repositoryRoot,
            template,
            calibrationOverlayPath);

        var reportPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase15",
            "epic-token-blueprints",
            "EPIC-BASE-TEMPLATE-R5.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        WriteReport(reportPath, repositoryRoot, template, templatePath);

        return new EpicBaseTemplateBuildResult
        {
            Template = template,
            TemplatePath = templatePath,
            CalibrationOverlayPath = calibrationOverlayPath,
            ReportPath = reportPath
        };
    }

    private static EpicBaseTemplateLayout BuildCalibratedLayout(
        string repositoryRoot,
        EpicTokenBlueprint source)
    {
        var dividerV = MapPhotoYToSurfaceV(DividerPhotoY);
        var surface = Bounds(
            SurfaceMinU,
            SurfaceMinV,
            SurfaceMaxU,
            SurfaceMaxV);
        var foreBounds = Bounds(
            SurfaceMinU,
            dividerV,
            SurfaceMaxU,
            SurfaceMaxV);
        var aftBounds = Bounds(
            SurfaceMinU,
            SurfaceMinV,
            SurfaceMaxU,
            dividerV);

        var foreCentre = Point(
            MapPhotoXToSurfaceU(ForeMountPhotoX),
            MapPhotoYToSurfaceV(ForeMountPhotoY));
        var aftCentre = Point(
            MapPhotoXToSurfaceU(AftMountPhotoX),
            MapPhotoYToSurfaceV(AftMountPhotoY));
        var guideRadius =
            (SurfaceMaxU - SurfaceMinU)
            * MountGuideRadiusSurfaceWidth;

        var photographPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "cr90corvette",
            "photos",
            "full.jpg");

        var photograph = AnalyseImage(photographPath);

        return new EpicBaseTemplateLayout
        {
            Status = "CommonEpicBaseTemplateCalibrated",
            UsesPhysicalTokenOutline = false,
            UsesPhysicalBaseGuideExtensions = false,
            RequiresTransparentCutouts = false,
            MeshUvBounds = source.Mesh.UvBounds,
            Calibration = new EpicBaseTemplateCalibration
            {
                ReferencePhotographAnalysis = photograph,
                MainRenderedSurface = new EpicTokenUvBounds
                {
                    MinU = SurfaceMinU,
                    MinV = SurfaceMinV,
                    MaxU = SurfaceMaxU,
                    MaxV = SurfaceMaxV
                },
                Notes = new List<string>
                {
                    "The OBJ UV island defines the rectangular rendered top surface.",
                    "The divider and two mount centres are normalized from full.jpg, then mapped into that UV island.",
                    "The photograph is calibration provenance only and is not embedded in the template artwork.",
                    "The guide-circle radius is a review value derived as 11% of the rendered surface width.",
                    "R5 values remain reviewable until confirmed in the diagnostic overlay and Tabletop Simulator."
                }
            },
            Sections = new List<EpicTokenSemanticSection>
            {
                new()
                {
                    Id = "fore",
                    Name = "Fore",
                    CalibrationStatus = "ReferenceCalibrated",
                    Bounds = foreBounds
                },
                new()
                {
                    Id = "aft",
                    Name = "Aft",
                    CalibrationStatus = "ReferenceCalibrated",
                    Bounds = aftBounds
                }
            },
            SectionDivider = new EpicTokenPendingLine
            {
                Id = "fore-aft-divider",
                Purpose =
                    "Printed visual divider between Fore and Aft artwork",
                CalibrationStatus = "ReferenceCalibrated",
                Start = Point(SurfaceMinU, dividerV),
                End = Point(SurfaceMaxU, dividerV)
            },
            ShipMountMarkers = new List<EpicTokenShipMountMarker>
            {
                new()
                {
                    Id = "fore-mount-marker",
                    Section = "Fore",
                    CalibrationStatus = "ReferenceCalibrated",
                    Centre = foreCentre,
                    GuideCircleRadius = guideRadius
                },
                new()
                {
                    Id = "aft-mount-marker",
                    Section = "Aft",
                    CalibrationStatus = "ReferenceCalibrated",
                    Centre = aftCentre,
                    GuideCircleRadius = guideRadius
                }
            },
            CommonRegions = new List<EpicTokenArtworkRegion>
            {
                Region(
                    "background",
                    "Both",
                    "Common background and starfield",
                    surface),
                Region(
                    "fore-firing-arcs",
                    "Fore",
                    "Reference-calibrated envelope for Fore firing-arc artwork",
                    foreBounds),
                Region(
                    "aft-firing-arcs",
                    "Aft",
                    "Reference-calibrated envelope for Aft firing-arc artwork",
                    aftBounds)
            },
            Notes = new List<string>
            {
                "This is the reusable Epic base texture template.",
                "It contains no ship name, ship icon, statistics, actions or ship-specific artwork.",
                "The generated texture is a rectangular 2048 x 2048 image.",
                "The Epic base OBJ and UV mapping provide the rendered base shape.",
                "The physical cardboard outline and large-base guide extensions are not reproduced.",
                "Printed ship-mount markers are guides, not holes or transparent cut-outs.",
                "R5 calibrates shared geometry only; CR90-specific regions remain pending."
            }
        };
    }

    private static EpicTokenImageAnalysis AnalyseImage(
        string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Epic calibration photograph was not found.",
                path);
        }

        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidDataException(
                $"Could not decode calibration photograph '{path}'.");

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(stream));

        const string repositoryMarker = "assets/source/";
        var normalised = path.Replace('\\', '/');
        var markerIndex = normalised.IndexOf(
            repositoryMarker,
            StringComparison.OrdinalIgnoreCase);

        return new EpicTokenImageAnalysis
        {
            RepositoryPath = markerIndex >= 0
                ? normalised[markerIndex..]
                : normalised,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Sha256 = hash
        };
    }

    private static double MapPhotoXToSurfaceU(
        double normalizedPhotoX) =>
        SurfaceMinU
        + normalizedPhotoX * (SurfaceMaxU - SurfaceMinU);

    private static double MapPhotoYToSurfaceV(
        double normalizedPhotoY) =>
        SurfaceMaxV
        - normalizedPhotoY * (SurfaceMaxV - SurfaceMinV);

    private static EpicTokenOptionalPoint Point(
        double u,
        double v) =>
        new()
        {
            U = u,
            V = v
        };

    private static EpicTokenOptionalBounds Bounds(
        double minU,
        double minV,
        double maxU,
        double maxV) =>
        new()
        {
            MinU = minU,
            MinV = minV,
            MaxU = maxU,
            MaxV = maxV
        };

    private static void WriteCalibrationOverlay(
        string repositoryRoot,
        EpicBaseTemplate template,
        string outputPath)
    {
        var templatePath = Path.Combine(
            repositoryRoot,
            template.Template.RepositoryPath.Replace(
                '/',
                Path.DirectorySeparatorChar));

        using var source = SKBitmap.Decode(templatePath)
            ?? throw new InvalidDataException(
                $"Could not decode Epic template '{templatePath}'.");
        using var bitmap = new SKBitmap(
            source.Width,
            source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.DrawBitmap(source, 0, 0);

        using var surfacePaint = Stroke(
            new SKColor(0, 255, 255, 230),
            5);
        using var dividerPaint = Stroke(
            new SKColor(0, 120, 255, 240),
            6);
        using var forePaint = Fill(
            new SKColor(0, 255, 255, 24));
        using var aftPaint = Fill(
            new SKColor(255, 0, 255, 20));
        using var markerPaint = Stroke(
            new SKColor(255, 220, 0, 245),
            5);
        markerPaint.PathEffect = SKPathEffect.CreateDash(
            new float[] { 18, 14 },
            0);
        using var crossPaint = Stroke(
            new SKColor(255, 220, 0, 255),
            4);

        var surface = template.Layout.Calibration.MainRenderedSurface;
        canvas.DrawRect(
            ToRect(surface, bitmap.Width, bitmap.Height),
            surfacePaint);

        var fore = template.Layout.Sections.Single(
            section => section.Id == "fore").Bounds!;
        var aft = template.Layout.Sections.Single(
            section => section.Id == "aft").Bounds!;
        canvas.DrawRect(
            ToRect(fore, bitmap.Width, bitmap.Height),
            forePaint);
        canvas.DrawRect(
            ToRect(aft, bitmap.Width, bitmap.Height),
            aftPaint);

        var divider = template.Layout.SectionDivider;
        canvas.DrawLine(
            ToX(divider.Start!.U, bitmap.Width),
            ToY(divider.Start.V, bitmap.Height),
            ToX(divider.End!.U, bitmap.Width),
            ToY(divider.End.V, bitmap.Height),
            dividerPaint);

        foreach (var marker in template.Layout.ShipMountMarkers)
        {
            var centre = marker.Centre!;
            var x = ToX(centre.U, bitmap.Width);
            var y = ToY(centre.V, bitmap.Height);
            var radius = (float)(
                marker.GuideCircleRadius!.Value
                * bitmap.Width);

            canvas.DrawCircle(
                x,
                y,
                radius,
                markerPaint);
            canvas.DrawLine(
                x - 24,
                y,
                x + 24,
                y,
                crossPaint);
            canvas.DrawLine(
                x,
                y - 24,
                x,
                y + 24,
                crossPaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Png,
            100);
        using var output = File.Create(outputPath);
        encoded.SaveTo(output);
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

    private static EpicTokenArtworkRegion Region(
        string id,
        string section,
        string purpose,
        EpicTokenOptionalBounds bounds) =>
        new()
        {
            Id = id,
            Section = section,
            Purpose = purpose,
            CalibrationStatus = "ReferenceCalibrated",
            Bounds = bounds
        };

    private static void WriteReport(
        string path,
        string repositoryRoot,
        EpicBaseTemplate template,
        string templatePath)
    {
        var lines = new List<string>
        {
            "# Epic Base Template — Phase 15A-R5",
            "",
            "This file defines only geometry and artwork regions shared by Epic ships.",
            "",
            $"Canvas: {template.Canvas.Width} × {template.Canvas.Height}",
            $"Mesh UV bounds: U {template.Mesh.UvBounds.MinU:F6}–{template.Mesh.UvBounds.MaxU:F6}, " +
            $"V {template.Mesh.UvBounds.MinV:F6}–{template.Mesh.UvBounds.MaxV:F6}",
            $"Sections: {template.Layout.Sections.Count}",
            $"Printed mount markers: {template.Layout.ShipMountMarkers.Count}",
            $"Common regions: {template.Layout.CommonRegions.Count}",
            $"Warnings: {template.ValidationWarnings.Count}",
            $"Calibration status: {template.Layout.Calibration.Status}",
            $"Main rendered surface: U {template.Layout.Calibration.MainRenderedSurface.MinU:F6}–{template.Layout.Calibration.MainRenderedSurface.MaxU:F6}, " +
            $"V {template.Layout.Calibration.MainRenderedSurface.MinV:F6}–{template.Layout.Calibration.MainRenderedSurface.MaxV:F6}",
            "",
            $"Output: `{Relative(repositoryRoot, templatePath)}`",
            $"Calibration overlay: `assets/source/unified1e/reference/epic/epic-base-template-calibration-overlay.png`",
            "",
            "No ship-specific artwork or final texture was generated."
        };

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Relative(
        string repositoryRoot,
        string path) =>
        Path.GetRelativePath(repositoryRoot, path)
            .Replace('\\', '/');
}

public static class EpicShipOverlayBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EpicShipOverlayBuildResult Build(
        string repositoryRoot,
        string shipId,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var source = EpicTokenBlueprintBuilder.Analyse(
            repositoryRoot,
            shipId);

        var overlay = new EpicShipOverlay
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            ShipId = source.ShipId,
            ShipName = source.ShipName,
            BaseTemplatePath =
                "assets/source/unified1e/reference/epic/epic-base-template.json",
            Sources = new EpicShipOverlaySources
            {
                ShipIcon = source.Sources.ShipIcon,
                ActionFont = source.Sources.ActionFont,
                ShipFont = source.Sources.ShipFont
            },
            Fonts = source.Fonts,
            Photographs = source.Photographs,
            ValidationWarnings = source.ValidationWarnings.ToList(),
            ShipRegions = new List<EpicTokenArtworkRegion>
            {
                Region(
                    "fore-ship-icon",
                    "Fore",
                    "Ship-specific icon"),
                Region(
                    "aft-ship-icon",
                    "Aft",
                    "Ship-specific icon"),
                Region(
                    "ship-title",
                    "Both",
                    "Ship-specific title treatment"),
                Region(
                    "fore-stat-panel",
                    "Fore",
                    "Fore section statistics"),
                Region(
                    "aft-stat-panel",
                    "Aft",
                    "Aft section statistics"),
                Region(
                    "fore-action-area",
                    "Fore",
                    "Fore action symbols"),
                Region(
                    "aft-action-area",
                    "Aft",
                    "Aft action symbols")
            },
            Notes = new List<string>
            {
                "This overlay contains only ship-specific semantic content.",
                "It depends on the common Epic base template.",
                "It does not duplicate common background, firing-arc, divider or mount-marker definitions.",
                "All coordinates remain PendingUvCalibration until measured.",
                "No final artwork or texture was generated."
            }
        };

        var defaultPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            source.ShipId,
            source.ShipId.Equals(
                "cr90corvette",
                StringComparison.OrdinalIgnoreCase)
                ? "cr90-ship-overlay.json"
                : $"{source.ShipId}-ship-overlay.json");
        var overlayPath = outputPath is null
            ? defaultPath
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(overlayPath)
            ?? throw new InvalidDataException(
                "Epic ship overlay output has no parent directory."));

        File.WriteAllText(
            overlayPath,
            JsonSerializer.Serialize(overlay, JsonOptions),
            new UTF8Encoding(false));

        var reportPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase15",
            "epic-token-blueprints",
            $"{source.ShipId.ToUpperInvariant()}-SHIP-OVERLAY-R4.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        WriteReport(reportPath, repositoryRoot, overlay, overlayPath);

        return new EpicShipOverlayBuildResult
        {
            Overlay = overlay,
            OverlayPath = overlayPath,
            ReportPath = reportPath
        };
    }

    private static EpicTokenArtworkRegion Region(
        string id,
        string section,
        string purpose) =>
        new()
        {
            Id = id,
            Section = section,
            Purpose = purpose
        };

    private static void WriteReport(
        string path,
        string repositoryRoot,
        EpicShipOverlay overlay,
        string overlayPath)
    {
        var lines = new List<string>
        {
            $"# {overlay.ShipName} Ship Overlay — Phase 15A-R4",
            "",
            "This file defines only ship-specific semantic regions and authoritative references.",
            "",
            $"- Ship: {overlay.ShipName} ({overlay.ShipId})",
            $"- Base template: `{overlay.BaseTemplatePath}`",
            $"- Ship-specific regions: {overlay.ShipRegions.Count}",
            $"- Reference photographs: {overlay.Photographs.Count}",
            $"- Fonts loaded: {overlay.Fonts.Count(font => font.LoadedSuccessfully)}/{overlay.Fonts.Count}",
            $"- Warnings: {overlay.ValidationWarnings.Count}",
            "",
            $"Output: `{Relative(repositoryRoot, overlayPath)}`",
            "",
            "No final artwork or texture was generated."
        };

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Relative(
        string repositoryRoot,
        string path) =>
        Path.GetRelativePath(repositoryRoot, path)
            .Replace('\\', '/');
}
