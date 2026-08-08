using System.Security.Cryptography;
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
        string? assetBaseUrl = null,
        string? shipId = null,
        bool compareSizes = false)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        referenceSavePath = Path.GetFullPath(referenceSavePath);
        ValidateFile(referenceSavePath, "Reference TTS save");

        var productionProfile = ResolveProductionProfile(
            repositoryRoot,
            shipId);

        if (compareSizes && productionProfile is not null)
        {
            throw new InvalidDataException(
                "--compare-sizes cannot be combined with --ship.");
        }

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

        texturePath ??= productionProfile?.TexturePath
            ?? Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "epic",
                "calibration",
                "epic-base-calibration.png");
        texturePath = Path.GetFullPath(texturePath);

        if (productionProfile is not null)
        {
            ValidateFile(
                texturePath,
                $"Locked {productionProfile.ShipName} production texture");
            ValidateLockedProductionTexture(
                texturePath,
                productionProfile);
            ValidateFile(
                productionProfile.ShipModelPath,
                $"{productionProfile.ShipName} model");
            ValidateFile(
                productionProfile.ShipTexturePath,
                $"{productionProfile.ShipName} model texture");
        }
        else if (!File.Exists(texturePath))
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

        var shortBaseMeshPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "bases",
            "epic",
            "base-short.obj");
        var shortPegMeshPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "bases",
            "pegs",
            "epic-short.obj");

        if (compareSizes)
        {
            ValidateFile(shortBaseMeshPath, "Short First Edition Epic base mesh");
            ValidateFile(shortPegMeshPath, "Short First Edition Epic peg mesh");
        }

        outputPath ??= compareSizes
            ? Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "validation",
                "epic",
                "epic-base-size-comparison.json")
            : productionProfile?.OutputPath
            ?? Path.Combine(
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

        root["SaveName"] = compareSizes
            ? "Unified 1E - Epic Long + Short Base Comparison"
            : productionProfile is null
            ? "Unified 1E - Epic Base Calibration"
            : $"Unified 1E - {productionProfile.ShipName} Base Validation";
        root["GameMode"] = "X-Wing First Edition";
        root["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt");
        root["EpochTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        root["Notes"] = compareSizes
            ? "Phase 15C First Edition Epic base-size validation.\n" +
              "LEFT: long CR90/Raider footprint. RIGHT: short Gozanti/C-ROC/GR-75 footprint.\n" +
              "The short base removes a nominal 30 mm from the physical centre span without scaling the end sections.\n" +
              "The short peg cylinders retain their original diameter and height and move inward by the same amount."
            : productionProfile is null
            ? "Phase 15B Epic base UV validation.\n" +
              "FORE should appear at the top. AFT should appear at the bottom.\n" +
              "Verify the blue divider and both yellow printed mount markers.\n" +
              "No final CR90 artwork is present."
            : productionProfile.Notes;

        JsonArray objects;

        if (compareSizes)
        {
            objects = new JsonArray
            {
                BuildEpicBaseObject(
                    AssetUrl(assetBaseUrl, meshRelative),
                    AssetUrl(assetBaseUrl, textureRelative),
                    AssetUrl(
                        assetBaseUrl,
                        "assets/source/unified1e/bases/pegs/epic.obj"),
                    AssetUrl(
                        assetBaseUrl,
                        "assets/source/unified1e/bases/epic/front/rebel.png"),
                    "Epic Long — CR90 / Raider",
                    "First Edition long Epic footprint.",
                    null,
                    null,
                    0.0,
                    "e9lng1",
                    "e9lp01",
                    -3.25),
                BuildEpicBaseObject(
                    AssetUrl(
                        assetBaseUrl,
                        Relative(repositoryRoot, shortBaseMeshPath)),
                    AssetUrl(assetBaseUrl, textureRelative),
                    AssetUrl(
                        assetBaseUrl,
                        Relative(repositoryRoot, shortPegMeshPath)),
                    AssetUrl(
                        assetBaseUrl,
                        "assets/source/unified1e/bases/epic/front/rebel.png"),
                    "Epic Short — Gozanti / C-ROC / GR-75",
                    "First Edition short Epic footprint; nominal 30 mm centre reduction.",
                    null,
                    null,
                    0.0,
                    "e9sht1",
                    "e9sp01",
                    3.25)
            };
        }
        else
        {
            objects = new JsonArray
            {
                BuildEpicBaseObject(
                    AssetUrl(assetBaseUrl, meshRelative),
                    AssetUrl(assetBaseUrl, textureRelative),
                    AssetUrl(
                        assetBaseUrl,
                        "assets/source/unified1e/bases/pegs/epic.obj"),
                    AssetUrl(
                        assetBaseUrl,
                        "assets/source/unified1e/bases/epic/front/rebel.png"),
                    productionProfile?.ObjectNickname ?? "Epic Base Calibration",
                    productionProfile?.ObjectDescription
                        ?? "Phase 15B calibration texture on the authoritative Epic base mesh.",
                    productionProfile is null
                        ? null
                        : AssetUrl(
                            assetBaseUrl,
                            productionProfile.ShipModelRepositoryPath),
                    productionProfile is null
                        ? null
                        : AssetUrl(
                            assetBaseUrl,
                            productionProfile.ShipTextureRepositoryPath),
                    productionProfile?.ShipHeight ?? 0.0),
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
        }

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
            compareSizes
                ? "epic-base-size-comparison-manifest.json"
                : productionProfile is null
                ? "epic-base-validation-manifest.json"
                : $"{productionProfile.ShipId}-base-validation-manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));

        var reportPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            compareSizes
                ? "EPIC-BASE-SIZE-COMPARISON.md"
                : productionProfile is null
                ? "EPIC-BASE-VALIDATION.md"
                : $"{productionProfile.ShipId.ToUpperInvariant()}-BASE-VALIDATION.md");
        WriteReport(
            reportPath,
            manifest,
            productionProfile,
            compareSizes);

        return manifest;
    }

    private static JsonObject BuildEpicBaseObject(
        string meshUrl,
        string textureUrl,
        string pegMeshUrl,
        string pegTextureUrl,
        string nickname,
        string description,
        string? shipMeshUrl,
        string? shipTextureUrl,
        double shipHeight,
        string guid = "e91cba",
        string pegGuid = "e9peg1",
        double posX = 0.0)
    {
        var childObjects = new JsonArray
        {
            BuildEpicPegObject(
                pegMeshUrl,
                pegTextureUrl,
                pegGuid)
        };

        if (!string.IsNullOrWhiteSpace(shipMeshUrl)
            && !string.IsNullOrWhiteSpace(shipTextureUrl))
        {
            childObjects.Add(
                BuildEpicShipObject(
                    shipMeshUrl,
                    shipTextureUrl,
                    shipHeight));
        }

        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Model",
            ["Transform"] = new JsonObject
            {
                ["posX"] = posX,
                ["posY"] = 1.0,
                ["posZ"] = 0.0,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 1.0,
                ["scaleY"] = 1.0,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = nickname,
            ["Description"] = description,
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
            ["ChildObjects"] = childObjects,
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static JsonObject BuildEpicPegObject(
        string pegMeshUrl,
        string pegTextureUrl,
        string guid = "e9peg1") =>
        new()
        {
            ["GUID"] = guid,
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
        };

    private static JsonObject BuildEpicShipObject(
        string shipMeshUrl,
        string shipTextureUrl,
        double shipHeight) =>
        new()
        {
            ["GUID"] = "c90shp",
            ["Name"] = "Custom_Model",
            ["Transform"] = new JsonObject
            {
                ["posX"] = 0.0,
                ["posY"] = shipHeight,
                ["posZ"] = 0.0,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 1.0,
                ["scaleY"] = 1.0,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = "CR90 Corvette",
            ["Description"] =
                "Repository-owned First Edition CR90 miniature at the authoritative Epic peg-top height.",
            ["ColorDiffuse"] = Colour(1, 1, 1),
            ["Locked"] = true,
            ["Grid"] = false,
            ["Snap"] = false,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = false,
            ["Autoraise"] = false,
            ["Sticky"] = true,
            ["Tooltip"] = true,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomMesh"] = new JsonObject
            {
                ["MeshURL"] = shipMeshUrl,
                ["DiffuseURL"] = shipTextureUrl,
                ["NormalURL"] = string.Empty,
                ["ColliderURL"] = shipMeshUrl,
                ["Convex"] = true,
                ["MaterialIndex"] = 1,
                ["TypeIndex"] = 1,
                ["CastShadows"] = true
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
        EpicBaseValidationSaveManifest manifest,
        ProductionValidationProfile? productionProfile,
        bool compareSizes)
    {
        var lines = new List<string>
        {
            compareSizes
                ? "# Epic Base Size Comparison — Phase 15C"
                : productionProfile is null
                ? "# Epic Base Validation — Phase 15B"
                : $"# {productionProfile.ShipName} Base Validation — Locked Production Artwork",
            "",
            "## Validate in Tabletop Simulator",
            ""
        };

        if (compareSizes)
        {
            lines.Add("- LEFT is the long CR90/Raider First Edition Epic footprint.");
            lines.Add("- RIGHT is the short Gozanti/C-ROC/GR-75 First Edition Epic footprint.");
            lines.Add("- The short base is 11.143134 mesh units long versus 12.885818 for the long base.");
            lines.Add("- Width, thickness, UVs and fore/aft end geometry remain unchanged.");
            lines.Add("- Short peg diameter and height remain unchanged; only fore/aft separation is reduced.");
            lines.Add("- Short mesh: `assets/source/unified1e/bases/epic/base-short.obj`.");
            lines.Add("- Short peg mesh: `assets/source/unified1e/bases/pegs/epic-short.obj`.");
        }
        else if (productionProfile is null)
        {
            lines.Add("- FORE label is at the top of the base.");
            lines.Add("- AFT label is at the bottom of the base.");
            lines.Add("- The blue divider crosses the intended Fore/Aft boundary.");
            lines.Add("- Both dashed yellow mount markers appear on the top surface.");
            lines.Add("- The cyan rendered-surface boundary follows the top UV island.");
            lines.Add("- No texture artwork appears on unintended mesh faces.");
        }
        else
        {
            lines.Add("- CR90 FORE artwork is at the top and AFT artwork is at the bottom.");
            lines.Add("- Both restored stat/name bars reach the intended base edges.");
            lines.Add("- Firing geometry, turret, action icons and white ship silhouette match the locked artwork.");
            lines.Add("- The blue divider crosses the intended Fore/Aft boundary.");
            lines.Add("- The repository-owned CR90 miniature is centred on both Epic pegs and sits at the peg-top height.");
            lines.Add("- No texture artwork appears on unintended mesh faces.");
        }

        lines.Add("");
        lines.Add($"Save: `{manifest.SavePath}`");
        lines.Add($"Mesh: `{manifest.BaseMesh}`");
        lines.Add($"Peg mesh: `{manifest.PegMesh}`");
        lines.Add($"Mount points: `{manifest.MountPointDatabase}`");
        lines.Add($"Texture: `{manifest.CalibrationTexture}`");
        lines.Add($"Objects: {manifest.ObjectCount}");

        if (productionProfile is not null)
        {
            lines.Add("");
            lines.Add($"Locked texture: `{productionProfile.TextureRepositoryPath}`");
            lines.Add($"Locked SHA-256: `{productionProfile.ExpectedSha256}`");
            lines.Add("The locked production texture is used directly; no generated targeting revision is substituted.");
        }

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

    private static ProductionValidationProfile? ResolveProductionProfile(
        string repositoryRoot,
        string? shipId)
    {
        if (string.IsNullOrWhiteSpace(shipId))
            return null;

        if (!shipId.Equals(
                "cr90corvette",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"No locked Epic production texture is registered for '{shipId}'.");
        }

        const string textureRepositoryPath =
            "assets/source/unified1e/pilot-tokens/" +
            "rebelalliance/cr90corvette/cr90corvette.png";

        return new ProductionValidationProfile(
            "cr90corvette",
            "CR90 Corvette",
            textureRepositoryPath,
            "14FBBB5900D72FEE481BFCC59C0860596A25AB5B1A0784108C8C03BD982FE57F",
            Path.Combine(
                repositoryRoot,
                textureRepositoryPath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "validation",
                "epic",
                "cr90corvette-base-validation.json"),
            "assets/source/unified1e/ships/epic/cr90corvette/cr90.obj",
            Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified1e",
                "ships",
                "epic",
                "cr90corvette",
                "cr90.obj"),
            "assets/source/unified1e/ships/epic/cr90corvette/Textures/standard.jpg",
            Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified1e",
                "ships",
                "epic",
                "cr90corvette",
                "Textures",
                "standard.jpg"),
            4.509502,
            "CR90 Corvette — Locked First Edition Base",
            "Locked First Edition CR90 base-token artwork on the authoritative Epic base mesh.",
            "Phase 15C locked CR90 production-artwork validation.\n" +
            "This save uses the manually restored First Edition CR90 base texture directly.\n" +
            "The repository-owned First Edition CR90 miniature is mounted at the measured Epic peg-top height.\n" +
            "Verify Fore/Aft orientation, stat bars, firing geometry, turret, action icons, ship silhouette and UV coverage.\n" +
            "The Phase 15C-R15 generated targeting texture is deliberately not used.");
    }

    private static void ValidateLockedProductionTexture(
        string texturePath,
        ProductionValidationProfile profile)
    {
        using var stream = File.OpenRead(texturePath);
        var actualSha256 = Convert.ToHexString(
            SHA256.HashData(stream));

        if (!actualSha256.Equals(
                profile.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The locked {profile.ShipName} production texture has changed. " +
                $"Expected SHA-256 {profile.ExpectedSha256}, got {actualSha256}.");
        }
    }

    private sealed record ProductionValidationProfile(
        string ShipId,
        string ShipName,
        string TextureRepositoryPath,
        string ExpectedSha256,
        string TexturePath,
        string OutputPath,
        string ShipModelRepositoryPath,
        string ShipModelPath,
        string ShipTextureRepositoryPath,
        string ShipTexturePath,
        double ShipHeight,
        string ObjectNickname,
        string ObjectDescription,
        string Notes);
}
