using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicBaseValidationSaveGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EpicBaseValidationSaveManifest Generate(
        string repositoryRoot,
        string referenceSavePath,
        string? templatePath = null,
        string? texturePath = null,
        string? outputPath = null,
        string? assetBaseUrl = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        referenceSavePath = Path.GetFullPath(referenceSavePath);
        ValidateFile(referenceSavePath, "Reference TTS save");

        assetBaseUrl ??=
            "https://raw.githubusercontent.com/grimsdalee/" +
            "xwing-unified-1e/main/";

        templatePath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-template.json");
        templatePath = Path.GetFullPath(templatePath);
        ValidateFile(templatePath, "Epic base template");

        texturePath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "epic",
            "calibration",
            "epic-base-calibration.png");
        texturePath = Path.GetFullPath(texturePath);

        if (!File.Exists(texturePath))
        {
            EpicBaseCalibrationTextureGenerator.Generate(
                repositoryRoot,
                templatePath,
                texturePath);
        }

        var baseMeshPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "bases",
            "epic",
            "base.obj");
        ValidateFile(baseMeshPath, "Epic base mesh");

        outputPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "validation",
            "epic",
            "epic-base-validation.json");
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException(
                "Epic validation save output has no parent directory."));

        var root = JsonNode.Parse(
            File.ReadAllText(referenceSavePath))
            ?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the reference TTS save.");

        var meshRelative = Relative(repositoryRoot, baseMeshPath);
        var textureRelative = Relative(repositoryRoot, texturePath);

        root["SaveName"] = "Unified 1E - Epic Base Calibration";
        root["GameMode"] = "X-Wing First Edition";
        root["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt");
        root["EpochTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        root["Notes"] =
            "Phase 15B Epic base UV validation.\n" +
            "FORE should appear at the top. AFT should appear at the bottom.\n" +
            "Verify the blue divider and both yellow printed mount markers.\n" +
            "No final CR90 artwork is present.";

        var objects = new JsonArray
        {
            BuildEpicBaseObject(
                AssetUrl(assetBaseUrl, meshRelative),
                AssetUrl(assetBaseUrl, textureRelative),
                AssetUrl(
                    assetBaseUrl,
                    "assets/source/unified1e/bases/pegs/epic.obj"),
                AssetUrl(
                    assetBaseUrl,
                    "assets/source/unified1e/bases/epic/front/rebel.png")),
            BuildMarkerCube(
                "f0a001",
                "FORE / +Z",
                0,
                1.1,
                8.0,
                0.1,
                0.6,
                2.5,
                0.15,
                0.65,
                1.0),
            BuildMarkerCube(
                "a0f002",
                "AFT / -Z",
                0,
                1.1,
                -8.0,
                0.1,
                0.6,
                2.5,
                0.75,
                0.15,
                0.85),
            BuildMarkerCube(
                "1ef003",
                "LEFT / -X",
                -6.0,
                1.1,
                0,
                2.0,
                0.6,
                0.1,
                0.15,
                0.85,
                0.45),
            BuildMarkerCube(
                "2ef004",
                "RIGHT / +X",
                6.0,
                1.1,
                0,
                2.0,
                0.6,
                0.1,
                0.9,
                0.45,
                0.1)
        };

        root["ObjectStates"] = objects;

        File.WriteAllText(
            outputPath,
            root.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
            new UTF8Encoding(false));

        var manifest = new EpicBaseValidationSaveManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = RelativeOrAbsolute(repositoryRoot, repositoryRoot),
            ReferenceSave = RelativeOrAbsolute(
                repositoryRoot,
                referenceSavePath),
            BaseTemplate = Relative(repositoryRoot, templatePath),
            CalibrationTexture = textureRelative,
            BaseMesh = meshRelative,
            PegMesh = "assets/source/unified1e/bases/pegs/epic.obj",
            MountPointDatabase =
                "assets/source/unified1e/reference/epic/" +
                "epic-base-mount-points.json",
            AssetBaseUrl = assetBaseUrl,
            SavePath = Relative(repositoryRoot, outputPath),
            ObjectCount = objects.Count
        };

        var manifestPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            "epic-base-validation-manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));

        var reportPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            "EPIC-BASE-VALIDATION.md");
        WriteReport(reportPath, manifest);

        return manifest;
    }

    private static JsonObject BuildEpicBaseObject(
        string meshUrl,
        string textureUrl,
        string pegMeshUrl,
        string pegTextureUrl) =>
        new()
        {
            ["GUID"] = "e91cba",
            ["Name"] = "Custom_Model",
            ["Transform"] = new JsonObject
            {
                ["posX"] = 0.0,
                ["posY"] = 1.0,
                ["posZ"] = 0.0,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 1.0,
                ["scaleY"] = 1.0,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = "Epic Base Calibration",
            ["Description"] =
                "Phase 15B calibration texture on the authoritative Epic base mesh.",
            ["ColorDiffuse"] = Colour(1, 1, 1),
            ["Locked"] = true,
            ["Grid"] = true,
            ["Snap"] = true,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = true,
            ["Autoraise"] = true,
            ["Sticky"] = true,
            ["Tooltip"] = true,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomMesh"] = new JsonObject
            {
                ["MeshURL"] = meshUrl,
                ["DiffuseURL"] = textureUrl,
                ["NormalURL"] = string.Empty,
                ["ColliderURL"] = meshUrl,
                ["Convex"] = true,
                ["MaterialIndex"] = 1,
                ["TypeIndex"] = 1,
                ["CastShadows"] = true
            },
            ["ChildObjects"] = new JsonArray
            {
                new JsonObject
                {
                    ["GUID"] = "e9peg1",
                    ["Name"] = "Custom_Model",
                    ["Transform"] = new JsonObject
                    {
                        ["posX"] = 0.0,
                        ["posY"] = 0.0,
                        ["posZ"] = 0.0,
                        ["rotX"] = 0.0,
                        ["rotY"] = 0.0,
                        ["rotZ"] = 0.0,
                        ["scaleX"] = 1.0,
                        ["scaleY"] = 1.0,
                        ["scaleZ"] = 1.0
                    },
                    ["Nickname"] = "Epic Pegs",
                    ["Description"] =
                        "Authoritative combined Fore/Aft Epic peg mesh.",
                    ["ColorDiffuse"] = Colour(1, 1, 1),
                    ["Locked"] = true,
                    ["Grid"] = true,
                    ["Snap"] = true,
                    ["IgnoreFoW"] = false,
                    ["MeasureMovement"] = false,
                    ["DragSelectable"] = true,
                    ["Autoraise"] = true,
                    ["Sticky"] = true,
                    ["Tooltip"] = true,
                    ["GridProjection"] = false,
                    ["HideWhenFaceDown"] = false,
                    ["Hands"] = false,
                    ["CustomMesh"] = new JsonObject
                    {
                        ["MeshURL"] = pegMeshUrl,
                        ["DiffuseURL"] = pegTextureUrl,
                        ["NormalURL"] = string.Empty,
                        ["ColliderURL"] = pegMeshUrl,
                        ["Convex"] = true,
                        ["MaterialIndex"] = 1,
                        ["TypeIndex"] = 1,
                        ["CastShadows"] = true
                    },
                    ["LuaScript"] = string.Empty,
                    ["LuaScriptState"] = string.Empty,
                    ["XmlUI"] = string.Empty
                }
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };

    private static JsonObject BuildMarkerCube(
        string guid,
        string nickname,
        double x,
        double y,
        double z,
        double scaleX,
        double scaleY,
        double scaleZ,
        double r,
        double g,
        double b) =>
        new()
        {
            ["GUID"] = guid,
            ["Name"] = "BlockSquare",
            ["Transform"] = new JsonObject
            {
                ["posX"] = x,
                ["posY"] = y,
                ["posZ"] = z,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = scaleX,
                ["scaleY"] = scaleY,
                ["scaleZ"] = scaleZ
            },
            ["Nickname"] = nickname,
            ["Description"] = string.Empty,
            ["ColorDiffuse"] = Colour(r, g, b),
            ["Locked"] = true,
            ["Grid"] = true,
            ["Snap"] = true,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = true,
            ["Autoraise"] = true,
            ["Sticky"] = true,
            ["Tooltip"] = true,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };

    private static JsonObject Colour(
        double r,
        double g,
        double b) =>
        new()
        {
            ["r"] = r,
            ["g"] = g,
            ["b"] = b,
            ["a"] = 1.0
        };

    private static string AssetUrl(
        string assetBaseUrl,
        string relativePath) =>
        assetBaseUrl.TrimEnd('/')
        + "/"
        + relativePath.TrimStart('/');

    private static string Relative(
        string repositoryRoot,
        string path) =>
        Path.GetRelativePath(repositoryRoot, path)
            .Replace('\\', '/');

    private static string RelativeOrAbsolute(
        string repositoryRoot,
        string path)
    {
        var relative = Relative(repositoryRoot, path);
        return relative.StartsWith("../", StringComparison.Ordinal)
            ? path.Replace('\\', '/')
            : relative;
    }

    private static void WriteReport(
        string path,
        EpicBaseValidationSaveManifest manifest)
    {
        var lines = new List<string>
        {
            "# Epic Base Validation — Phase 15B",
            "",
            "## Validate in Tabletop Simulator",
            "",
            "- FORE label is at the top of the base.",
            "- AFT label is at the bottom of the base.",
            "- The blue divider crosses the intended Fore/Aft boundary.",
            "- Both dashed yellow mount markers appear on the top surface.",
            "- The cyan rendered-surface boundary follows the top UV island.",
            "- No texture artwork appears on unintended mesh faces.",
            "",
            $"Save: `{manifest.SavePath}`",
            $"Mesh: `{manifest.BaseMesh}`",
            $"Peg mesh: `{manifest.PegMesh}`",
            $"Mount points: `{manifest.MountPointDatabase}`",
            $"Texture: `{manifest.CalibrationTexture}`",
            $"Objects: {manifest.ObjectCount}"
        };

        File.WriteAllLines(
            path,
            lines,
            new UTF8Encoding(false));
    }

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
