using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicTokenBlueprintBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EpicTokenBlueprint Analyse(
        string repositoryRoot,
        string shipId)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        shipId = NormaliseShipId(shipId);

        if (!shipId.Equals("cr90corvette", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Phase 15A-R3 currently supports only 'cr90corvette', not '{shipId}'.");
        }

        var meshPath = RequireFile(
            repositoryRoot,
            "assets/source/unified1e/bases/epic/base.obj",
            "Epic base mesh");
        var templatePath = RequireFile(
            repositoryRoot,
            "assets/source/unified1e/bases/epic/front/rebel.png",
            "Epic Rebel UV template");
        var iconPath = RequireFile(
            repositoryRoot,
            "assets/source/unified1e/ships/epic/cr90corvette/icon.png",
            "CR90 ship icon");
        var actionFontPath = RequireFile(
            repositoryRoot,
            "assets/source/unified1e/reference/fonts/xwing-miniatures.ttf",
            "X-Wing action font");
        var shipFontPath = RequireFile(
            repositoryRoot,
            "assets/source/unified1e/reference/fonts/xwing-miniatures-ships.ttf",
            "X-Wing ship font");

        var template = AnalyseImage(repositoryRoot, templatePath);
        var icon = AnalyseImage(repositoryRoot, iconPath);
        var mesh = AnalyseObj(meshPath);
        var actionFont = AnalyseFont(repositoryRoot, actionFontPath);
        var shipFont = AnalyseFont(repositoryRoot, shipFontPath);
        var photographs = DiscoverPhotographs(repositoryRoot, shipId);

        var blueprint = new EpicTokenBlueprint
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            ShipId = shipId,
            ShipName = "CR90 Corvette",
            Canvas = new EpicTokenCanvas
            {
                Width = template.Width,
                Height = template.Height
            },
            Sources = new EpicTokenSourceAssets
            {
                Mesh = Relative(repositoryRoot, meshPath),
                TemplateTexture = Relative(repositoryRoot, templatePath),
                ShipIcon = Relative(repositoryRoot, iconPath),
                ActionFont = Relative(repositoryRoot, actionFontPath),
                ShipFont = Relative(repositoryRoot, shipFontPath)
            },
            Mesh = mesh,
            Template = template,
            ShipIcon = icon,
            Fonts = new List<EpicTokenFontAnalysis>
            {
                actionFont,
                shipFont
            },
            Photographs = photographs,
            Layout = BuildRectangularTextureLayout(mesh)
        };

        if (template.Width != 2048 || template.Height != 2048)
        {
            blueprint.ValidationWarnings.Add(
                $"Expected a 2048 x 2048 UV template, found " +
                $"{template.Width} x {template.Height}.");
        }

        if (photographs.Count == 0)
        {
            blueprint.ValidationWarnings.Add(
                "No CR90 reference photographs were found. Photographs are " +
                "reference material only and do not define texture geometry.");
        }

        return blueprint;
    }

    public static EpicTokenBlueprintBuildResult Build(
        string repositoryRoot,
        string shipId,
        string? outputFolder = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        shipId = NormaliseShipId(shipId);

        var blueprint = Analyse(
            repositoryRoot,
            shipId);

        var referenceFolder = outputFolder is null
            ? Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified1e",
                "reference",
                "epic",
                shipId)
            : Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(referenceFolder);

        var blueprintPath = Path.Combine(
            referenceFolder,
            "cr90-base-token-blueprint.json");
        File.WriteAllText(
            blueprintPath,
            JsonSerializer.Serialize(blueprint, JsonOptions),
            new UTF8Encoding(false));

        var reportFolder = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase15",
            "epic-token-blueprints");
        Directory.CreateDirectory(reportFolder);
        var reportPath = Path.Combine(
            reportFolder,
            "CR90-BASE-TOKEN-BLUEPRINT-R3.md");
        WriteReport(
            reportPath,
            repositoryRoot,
            blueprint,
            blueprintPath);

        return new EpicTokenBlueprintBuildResult
        {
            Blueprint = blueprint,
            BlueprintPath = blueprintPath,
            ReportPath = reportPath
        };
    }

    private static EpicTokenTextureLayout BuildRectangularTextureLayout(
        EpicTokenMeshAnalysis mesh)
    {
        return new EpicTokenTextureLayout
        {
            UsesPhysicalTokenOutline = false,
            UsesPhysicalBaseGuideExtensions = false,
            RequiresTransparentCutouts = false,
            MeshUvBounds = mesh.UvBounds,
            ShipMountMarkers = new List<EpicTokenShipMountMarker>
            {
                new()
                {
                    Id = "fore-mount-marker",
                    Section = "Fore"
                },
                new()
                {
                    Id = "aft-mount-marker",
                    Section = "Aft"
                }
            },
            ArtworkRegions = new List<EpicTokenArtworkRegion>
            {
                Region("background", "Both", "Starfield and common background"),
                Region("fore-firing-arcs", "Fore", "Fore firing-arc artwork"),
                Region("aft-firing-arcs", "Aft", "Aft firing-arc artwork"),
                Region("fore-ship-icon", "Fore", "CR90 ship icon"),
                Region("aft-ship-icon", "Aft", "CR90 ship icon"),
                Region("ship-title", "Both", "CR90 Corvette title treatment"),
                Region("fore-stat-panel", "Fore", "Fore section statistics"),
                Region("aft-stat-panel", "Aft", "Aft section statistics"),
                Region("fore-action-area", "Fore", "Fore action symbols"),
                Region("aft-action-area", "Aft", "Aft action symbols")
            },
            Notes = new List<string>
            {
                "The generated texture is a normal rectangular 2048 x 2048 image.",
                "The Epic base OBJ and its UV mapping provide the rendered base shape.",
                "The extended centre sections on the physical cardboard token are guides for two physical large bases and are not reproduced.",
                "Ship mount markers are printed guide circles only; they are not holes, cut-outs or transparent regions.",
                "Reference photographs are used only to calibrate artwork, colours, typography and spacing.",
                "No semantic region coordinates are guessed in R3. All positions remain PendingUvCalibration until measured against the authoritative template and photographs.",
                "No final token artwork or diagnostic images are generated by R3."
            }
        };
    }

    private static EpicTokenArtworkRegion Region(
        string id,
        string section,
        string purpose) => new()
    {
        Id = id,
        Section = section,
        Purpose = purpose
    };

    private static EpicTokenMeshAnalysis AnalyseObj(string path)
    {
        var textureCoordinates = new List<EpicTokenUvPoint>();
        var segments = new HashSet<string>(StringComparer.Ordinal);
        var segmentList = new List<EpicTokenUvSegment>();
        var vertices = 0;
        var normals = 0;
        var faces = 0;
        var texturedFaces = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                vertices++;
                continue;
            }
            if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                normals++;
                continue;
            }
            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                var values = line[3..].Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 2
                    && double.TryParse(
                        values[0],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var u)
                    && double.TryParse(
                        values[1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var v))
                {
                    textureCoordinates.Add(new EpicTokenUvPoint
                    {
                        U = u,
                        V = v
                    });
                }
                continue;
            }
            if (!line.StartsWith("f ", StringComparison.Ordinal))
                continue;

            faces++;
            var tokens = line[2..].Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);
            var faceUv = new List<int>();
            foreach (var token in tokens)
            {
                var parts = token.Split('/');
                if (parts.Length <= 1
                    || !int.TryParse(
                        parts[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var textureIndex)
                    || textureIndex == 0)
                {
                    continue;
                }

                var resolved = textureIndex > 0
                    ? textureIndex - 1
                    : textureCoordinates.Count + textureIndex;
                if (resolved >= 0 && resolved < textureCoordinates.Count)
                    faceUv.Add(resolved);
            }

            if (faceUv.Count < 2)
                continue;

            texturedFaces++;
            for (var index = 0; index < faceUv.Count; index++)
            {
                var aIndex = faceUv[index];
                var bIndex = faceUv[(index + 1) % faceUv.Count];
                var low = Math.Min(aIndex, bIndex);
                var high = Math.Max(aIndex, bIndex);
                if (!segments.Add($"{low}:{high}"))
                    continue;

                segmentList.Add(new EpicTokenUvSegment
                {
                    Start = textureCoordinates[aIndex],
                    End = textureCoordinates[bIndex]
                });
            }
        }

        if (textureCoordinates.Count == 0)
        {
            throw new InvalidDataException(
                "Epic base OBJ contains no texture coordinates.");
        }

        return new EpicTokenMeshAnalysis
        {
            VertexCount = vertices,
            TextureCoordinateCount = textureCoordinates.Count,
            NormalCount = normals,
            FaceCount = faces,
            FacesWithTextureCoordinates = texturedFaces,
            UvBounds = new EpicTokenUvBounds
            {
                MinU = textureCoordinates.Min(point => point.U),
                MinV = textureCoordinates.Min(point => point.V),
                MaxU = textureCoordinates.Max(point => point.U),
                MaxV = textureCoordinates.Max(point => point.V)
            },
            UvSegments = segmentList,
            Sha256 = Hash(path)
        };
    }

    private static EpicTokenImageAnalysis AnalyseImage(
        string repositoryRoot,
        string path)
    {
        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidDataException(
                $"Could not decode image '{path}'.");
        return new EpicTokenImageAnalysis
        {
            RepositoryPath = Relative(repositoryRoot, path),
            Width = bitmap.Width,
            Height = bitmap.Height,
            Sha256 = Hash(path)
        };
    }

    private static EpicTokenFontAnalysis AnalyseFont(
        string repositoryRoot,
        string path)
    {
        using var typeface = SKTypeface.FromFile(path);
        return new EpicTokenFontAnalysis
        {
            RepositoryPath = Relative(repositoryRoot, path),
            FamilyName = typeface?.FamilyName ?? string.Empty,
            Sha256 = Hash(path),
            LoadedSuccessfully = typeface is not null
        };
    }

    private static List<string> DiscoverPhotographs(
        string repositoryRoot,
        string shipId)
    {
        var photographsRoot = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            shipId,
            "photos");

        if (!Directory.Exists(photographsRoot))
            return new List<string>();

        var extensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".webp" },
            StringComparer.OrdinalIgnoreCase);

        return Directory
            .EnumerateFiles(
                photographsRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Select(path => Relative(repositoryRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteReport(
        string path,
        string repositoryRoot,
        EpicTokenBlueprint blueprint,
        string blueprintPath)
    {
        var pendingRegions = blueprint.Layout.ArtworkRegions.Count(region =>
            region.CalibrationStatus.Equals(
                "PendingUvCalibration",
                StringComparison.OrdinalIgnoreCase));
        var pendingMarkers = blueprint.Layout.ShipMountMarkers.Count(marker =>
            marker.CalibrationStatus.Equals(
                "PendingUvCalibration",
                StringComparison.OrdinalIgnoreCase));

        var lines = new List<string>
        {
            "# CR90 Epic Base Texture Blueprint — Phase 15A-R3",
            "",
            "R3 replaces the physical-cardboard model with a rectangular texture blueprint intended for the Epic base OBJ.",
            "",
            "## Authoritative inputs",
            "",
            $"- Mesh: `{blueprint.Sources.Mesh}`",
            $"- UV/template texture: `{blueprint.Sources.TemplateTexture}`",
            $"- Ship icon: `{blueprint.Sources.ShipIcon}`",
            $"- Action font: `{blueprint.Sources.ActionFont}`",
            $"- Ship font: `{blueprint.Sources.ShipFont}`",
            $"- Reference photographs: {blueprint.Photographs.Count}",
            "",
            "## Texture architecture",
            "",
            $"- Canvas: {blueprint.Canvas.Width} × {blueprint.Canvas.Height}",
            $"- Canvas shape: {blueprint.Canvas.Shape}",
            $"- Texture mode: {blueprint.Canvas.TextureMode}",
            $"- Layout status: {blueprint.Layout.Status}",
            $"- Physical token outline used: {blueprint.Layout.UsesPhysicalTokenOutline}",
            $"- Physical base-guide extensions used: {blueprint.Layout.UsesPhysicalBaseGuideExtensions}",
            $"- Transparent cut-outs required: {blueprint.Layout.RequiresTransparentCutouts}",
            "",
            "## Semantic layout",
            "",
            $"- Sections: {blueprint.Layout.ForeSection.Name}, {blueprint.Layout.AftSection.Name}",
            $"- Printed ship-mount markers: {blueprint.Layout.ShipMountMarkers.Count}",
            $"- Named artwork regions: {blueprint.Layout.ArtworkRegions.Count}",
            $"- Markers pending UV calibration: {pendingMarkers}",
            $"- Regions pending UV calibration: {pendingRegions}",
            $"- Divider status: {blueprint.Layout.SectionDivider.CalibrationStatus}",
            "",
            "## Output",
            "",
            $"- Blueprint: `{Relative(repositoryRoot, blueprintPath)}`",
            "",
            "## R3 limitations",
            "",
            "- R3 does not generate final token artwork.",
            "- R3 does not generate diagnostic images.",
            "- R3 does not reproduce the physical cardboard outline or its large-base guide extensions.",
            "- R3 does not treat ship-mount markers as holes or transparent cut-outs.",
            "- R3 deliberately leaves all artwork coordinates pending rather than guessing them.",
            "",
            "## Next calibration step",
            "",
            "Measure the fore/aft divider, both printed mount markers, firing arcs, title, icon, statistics and action areas directly in the authoritative 2048 × 2048 UV/template coordinate space."
        };

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string RequireFile(
        string repositoryRoot,
        string repositoryPath,
        string description)
    {
        var fullPath = Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"{description} was not found.",
                fullPath);
        }
        return fullPath;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Relative(
        string repositoryRoot,
        string path) => Path
        .GetRelativePath(repositoryRoot, path)
        .Replace('\\', '/');

    private static string NormaliseShipId(string value) => new(
        value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
}
