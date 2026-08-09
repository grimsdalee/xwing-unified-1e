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
        bool compareSizes = false,
        bool allShips = false)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        referenceSavePath = Path.GetFullPath(referenceSavePath);
        ValidateFile(referenceSavePath, "Reference TTS save");

        if (allShips)
        {
            if (compareSizes || !string.IsNullOrWhiteSpace(shipId))
            {
                throw new InvalidDataException(
                    "--all-ships cannot be combined with --ship or --compare-sizes.");
            }

            return GenerateAllShipsComparison(
                repositoryRoot,
                referenceSavePath,
                outputPath,
                assetBaseUrl);
        }

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
            ValidateFile(
                Path.Combine(
                    repositoryRoot,
                    productionProfile.PegTextureRepositoryPath.Replace(
                        '/', Path.DirectorySeparatorChar)),
                $"{productionProfile.ShipName} Epic peg texture");

            if (productionProfile.PilotCards.Count > 0)
            {
                ValidateFile(
                    Path.Combine(
                        repositoryRoot,
                        productionProfile.PilotCardBackRepositoryPath.Replace(
                            '/', Path.DirectorySeparatorChar)),
                    $"{productionProfile.ShipName} First Edition pilot-card back");

                foreach (var pilotCard in productionProfile.PilotCards)
                {
                    ValidateFile(
                        Path.Combine(
                            repositoryRoot,
                            pilotCard.RepositoryPath.Replace(
                                '/', Path.DirectorySeparatorChar)),
                        $"{productionProfile.ShipName} {pilotCard.Section} pilot card");
                }
            }
        }
        else if (!File.Exists(texturePath))
        {
            EpicBaseCalibrationTextureGenerator.Generate(
                repositoryRoot,
                templatePath,
                texturePath);
        }

        var baseMeshRepositoryPath =
            productionProfile?.BaseMeshRepositoryPath
            ?? "assets/source/unified1e/bases/epic/base.obj";
        var pegMeshRepositoryPath =
            productionProfile?.PegMeshRepositoryPath
            ?? "assets/source/unified1e/bases/pegs/epic.obj";
        var baseMeshPath = Path.Combine(
            repositoryRoot,
            baseMeshRepositoryPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        var pegMeshPath = Path.Combine(
            repositoryRoot,
            pegMeshRepositoryPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        ValidateFile(baseMeshPath, "Epic base mesh");
        ValidateFile(pegMeshPath, "Epic peg mesh");

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
                    AssetUrl(assetBaseUrl, pegMeshRepositoryPath),
                    AssetUrl(
                        assetBaseUrl,
                        "assets/source/unified1e/bases/epic/front/rebel.png"),
                    "Epic Long — CR90 / Raider",
                    "First Edition long Epic footprint.",
                    null,
                    null,
                    0.0,
                    null,
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
                    null,
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
                    AssetUrl(assetBaseUrl, pegMeshRepositoryPath),
                    AssetUrl(
                        assetBaseUrl,
                        productionProfile?.PegTextureRepositoryPath
                            ?? "assets/source/unified1e/bases/epic/front/rebel.png"),
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
                    productionProfile?.ShipHeight ?? 0.0,
                    productionProfile),
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

            if (productionProfile is not null
                && productionProfile.PilotCards.Count > 0)
            {
                var cardBackUrl = AssetUrl(
                    assetBaseUrl,
                    productionProfile.PilotCardBackRepositoryPath);

                foreach (var pilotCard in productionProfile.PilotCards)
                {
                    objects.Add(
                        BuildPilotCardObject(
                            pilotCard.Guid,
                            pilotCard.Nickname,
                            $"{productionProfile.ShipName} — {pilotCard.Section}",
                            AssetUrl(assetBaseUrl, pilotCard.RepositoryPath),
                            cardBackUrl,
                            pilotCard.PositionX,
                            pilotCard.PositionZ));
                }
            }
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
            PegMesh = pegMeshRepositoryPath,
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

    private static EpicBaseValidationSaveManifest GenerateAllShipsComparison(
        string repositoryRoot,
        string referenceSavePath,
        string? outputPath,
        string? assetBaseUrl)
    {
        assetBaseUrl ??=
            "https://raw.githubusercontent.com/grimsdalee/" +
            "xwing-unified-1e/main/";

        var profiles = new[]
        {
            ResolveProductionProfile(repositoryRoot, "cr90corvette")!,
            ResolveProductionProfile(repositoryRoot, "raiderclasscorvette")!,
            ResolveProductionProfile(repositoryRoot, "gozanticlasscruiser")!,
            ResolveProductionProfile(repositoryRoot, "croccruiser")!,
            ResolveProductionProfile(repositoryRoot, "gr75mediumtransport")!
        };

        foreach (var profile in profiles)
        {
            ValidateFile(
                profile.TexturePath,
                $"Locked {profile.ShipName} production texture");
            ValidateLockedProductionTexture(profile.TexturePath, profile);
            ValidateFile(profile.ShipModelPath, $"{profile.ShipName} model");
            ValidateFile(
                profile.ShipTexturePath,
                $"{profile.ShipName} model texture");
            ValidateRepositoryFile(
                repositoryRoot,
                profile.BaseMeshRepositoryPath,
                $"{profile.ShipName} base mesh");
            ValidateRepositoryFile(
                repositoryRoot,
                profile.PegMeshRepositoryPath,
                $"{profile.ShipName} peg mesh");
            ValidateRepositoryFile(
                repositoryRoot,
                profile.PegTextureRepositoryPath,
                $"{profile.ShipName} peg texture");
        }

        outputPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "validation",
            "epic",
            "all-epic-ships-comparison.json");
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException(
                "All-Epic-ships output has no parent directory."));

        var root = JsonNode.Parse(File.ReadAllText(referenceSavePath))
            ?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the reference TTS save.");

        root["SaveName"] =
            "Unified 1E - All Five Epic Ships Comparison";
        root["GameMode"] = "X-Wing First Edition";
        root["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt");
        root["EpochTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        root["Notes"] =
            "Phase 15C locked First Edition Epic assembly comparison.\n" +
            "TOP: CR90 Corvette and Raider-class Corvette on long Epic bases.\n" +
            "BOTTOM: Gozanti, C-ROC and GR-75 on short Epic bases.\n" +
            "All five use repository-owned models, locked base-token textures and reference peg-top heights.";

        var placements = new[]
        {
            new EpicComparisonPlacement(profiles[0], -7.0, 7.5, "e5cr90", "p5cr90"),
            new EpicComparisonPlacement(profiles[1], 7.0, 7.5, "e5raid", "p5raid"),
            new EpicComparisonPlacement(profiles[2], -12.0, -8.0, "e5goza", "p5goza"),
            new EpicComparisonPlacement(profiles[3], 0.0, -8.0, "e5croc", "p5croc"),
            new EpicComparisonPlacement(profiles[4], 12.0, -8.0, "e5gr75", "p5gr75")
        };

        var objects = new JsonArray();
        foreach (var placement in placements)
        {
            var profile = placement.Profile;
            objects.Add(
                BuildEpicBaseObject(
                    AssetUrl(assetBaseUrl, profile.BaseMeshRepositoryPath),
                    AssetUrl(assetBaseUrl, profile.TextureRepositoryPath),
                    AssetUrl(assetBaseUrl, profile.PegMeshRepositoryPath),
                    AssetUrl(assetBaseUrl, profile.PegTextureRepositoryPath),
                    profile.ObjectNickname,
                    profile.ObjectDescription,
                    AssetUrl(assetBaseUrl, profile.ShipModelRepositoryPath),
                    AssetUrl(assetBaseUrl, profile.ShipTextureRepositoryPath),
                    profile.ShipHeight,
                    profile,
                    placement.BaseGuid,
                    placement.PegGuid,
                    placement.PositionX,
                    placement.PositionZ));
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
            BaseTemplate =
                "assets/source/unified1e/reference/epic/epic-base-template.json",
            CalibrationTexture = "Five locked production textures",
            BaseMesh = "Long + short First Edition Epic meshes",
            PegMesh = "Long + short First Edition Epic peg meshes",
            MountPointDatabase =
                "assets/source/unified1e/reference/epic/epic-base-mount-points.json",
            AssetBaseUrl = assetBaseUrl,
            SavePath = Relative(repositoryRoot, outputPath),
            ObjectCount = objects.Count
        };

        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "all-epic-ships-comparison-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));

        WriteAllShipsReport(
            Path.Combine(
                outputDirectory,
                "ALL-EPIC-SHIPS-COMPARISON.md"),
            manifest,
            profiles);

        return manifest;
    }

    private static void ValidateRepositoryFile(
        string repositoryRoot,
        string repositoryPath,
        string description) =>
        ValidateFile(
            Path.Combine(
                repositoryRoot,
                repositoryPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)),
            description);

    private static void WriteAllShipsReport(
        string path,
        EpicBaseValidationSaveManifest manifest,
        IReadOnlyList<ProductionValidationProfile> profiles)
    {
        var lines = new List<string>
        {
            "# All Five Epic Ships Comparison — Phase 15C",
            "",
            "## Assemblies",
            ""
        };

        foreach (var profile in profiles)
        {
            lines.Add(
                $"- {profile.ShipName}: `{profile.TextureRepositoryPath}` " +
                $"(`{profile.ExpectedSha256}`)");
        }

        lines.Add("");
        lines.Add("## Validate in Tabletop Simulator");
        lines.Add("");
        lines.Add("- CR90 and Raider use the long First Edition Epic base and peg spacing.");
        lines.Add("- Gozanti, C-ROC and GR-75 use the short First Edition Epic base and peg spacing.");
        lines.Add("- Compare model scale, height, orientation and overhang across all five assemblies.");
        lines.Add("- Compare base-token dimensions, dashboard orientation, peg placement and texture UV coverage.");
        lines.Add("");
        lines.Add($"Save: `{manifest.SavePath}`");
        lines.Add($"Objects: {manifest.ObjectCount}");

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
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
        ProductionValidationProfile? productionProfile = null,
        string guid = "e91cba",
        string pegGuid = "e9peg1",
        double posX = 0.0,
        double posZ = 0.0)
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
                    shipHeight,
                    productionProfile));
        }

        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Model",
            ["Transform"] = new JsonObject
            {
                ["posX"] = posX,
                ["posY"] = 1.0,
                ["posZ"] = posZ,
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
        double shipHeight,
        ProductionValidationProfile? productionProfile) =>
        new()
        {
            ["GUID"] = productionProfile?.ShipObjectGuid ?? "e9shp1",
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
            ["Nickname"] = productionProfile?.ShipName ?? "Epic Ship",
            ["Description"] = productionProfile?.ShipObjectDescription
                ?? "Repository-owned First Edition Epic miniature at the authoritative Epic peg-top height.",
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

    private static JsonObject BuildPilotCardObject(
        string guid,
        string nickname,
        string description,
        string imageUrl,
        string cardBackUrl,
        double positionX,
        double positionZ) =>
        new()
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Tile",
            ["Transform"] = new JsonObject
            {
                ["posX"] = positionX,
                ["posY"] = 1.0,
                ["posZ"] = positionZ,
                ["rotX"] = 0.0,
                ["rotY"] = 180.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 1.0,
                ["scaleY"] = 1.0,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = nickname,
            ["Description"] = description,
            ["ColorDiffuse"] = Colour(1, 1, 1),
            ["Locked"] = false,
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
            ["Hands"] = true,
            ["CustomImage"] = new JsonObject
            {
                ["ImageURL"] = imageUrl,
                ["ImageSecondaryURL"] = cardBackUrl,
                ["ImageScalar"] = 1.0,
                ["WidthScale"] = 0.0,
                ["CustomTile"] = new JsonObject
                {
                    ["Type"] = 0,
                    ["Thickness"] = 0.02,
                    ["Stackable"] = false,
                    ["Stretch"] = true
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
            if (productionProfile.ShipId.Equals(
                    "cr90corvette",
                    StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("- CR90 FORE artwork is at the top and AFT artwork is at the bottom.");
                lines.Add("- Both restored stat/name bars reach the intended base edges.");
                lines.Add("- Firing geometry, turret, action icons and white ship silhouette match the locked artwork.");
                lines.Add("- The blue divider crosses the intended Fore/Aft boundary.");
                lines.Add("- The repository-owned CR90 miniature is centred on both Epic pegs and sits at the peg-top height.");
                lines.Add("- No texture artwork appears on unintended mesh faces.");
            }
            else if (productionProfile.ShipId.Equals(
                         "raiderclasscorvette",
                         StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("- Raider FORE artwork is at the top and AFT artwork is at the bottom.");
                lines.Add("- Both restored stat/name bars reach the intended base edges.");
                lines.Add("- Green firing geometry, action icons and white ship silhouette match the locked Raider artwork.");
                lines.Add("- The blue divider crosses the intended Fore/Aft boundary without green line overlap.");
                lines.Add("- The repository-owned Raider miniature is centred on both Epic pegs and sits at the reference peg-top height.");
                lines.Add("- Both First Edition Raider section pilot cards are present beside the base.");
                lines.Add("- No texture artwork appears on unintended mesh faces.");
            }
            else if (productionProfile.ShipId.Equals(
                         "gozanticlasscruiser",
                         StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("- Gozanti artwork is correctly oriented with the dashboard at the Aft end.");
                lines.Add("- The restored stat/name bar reaches the intended base edges.");
                lines.Add("- The green Fore firing zone, action icons and white ship silhouette match the locked Gozanti artwork.");
                lines.Add("- The blue divider crosses the intended Fore/Aft boundary without green line overlap.");
                lines.Add("- The short First Edition Epic base and short peg mesh are used without scaling their end sections.");
                lines.Add("- The repository-owned Gozanti miniature is centred on both shortened Epic pegs and sits at the reference peg-top height.");
                lines.Add("- The First Edition Gozanti pilot card is present beside the base.");
                lines.Add("- No texture artwork appears on unintended mesh faces.");
            }
            else if (productionProfile.ShipId.Equals(
                         "croccruiser",
                         StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("- C-ROC artwork is correctly oriented with the dashboard at the Aft end.");
                lines.Add("- The yellow Fore firing zone, action icons and white ship silhouette match the locked C-ROC artwork.");
                lines.Add("- No blue divider is present, matching the physical First Edition C-ROC token.");
                lines.Add("- The short First Edition Epic base and short peg mesh are used without scaling their end sections.");
                lines.Add("- The repository-owned C-ROC miniature is centred on both shortened Epic pegs and sits at the reference peg-top height.");
                lines.Add("- The First Edition C-ROC pilot card is present beside the base.");
                lines.Add("- No texture artwork appears on unintended mesh faces.");
            }
            else
            {
                lines.Add("- GR-75 artwork is correctly oriented with the dashboard at the Aft end.");
                lines.Add("- The action icons and white ship silhouette match the locked GR-75 artwork.");
                lines.Add("- The blue divider crosses the intended Fore/Aft boundary.");
                lines.Add("- The short First Edition Epic base and short peg mesh are used without scaling their end sections.");
                lines.Add("- The repository-owned GR-75 miniature is centred on both shortened Epic pegs and sits at the reference peg-top height.");
                lines.Add("- The First Edition GR-75 pilot card is present beside the base.");
                lines.Add("- No texture artwork appears on unintended mesh faces.");
            }
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

        if (shipId.Equals(
                "cr90corvette",
                StringComparison.OrdinalIgnoreCase))
        {
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
                "assets/source/unified1e/bases/epic/base.obj",
                "assets/source/unified1e/bases/pegs/epic.obj",
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
                "assets/source/unified1e/bases/epic/front/rebel.png",
                4.509502,
                "c90shp",
                "Repository-owned First Edition CR90 miniature at the authoritative Epic peg-top height.",
                "CR90 Corvette — Locked First Edition Base",
                "Locked First Edition CR90 base-token artwork on the authoritative Epic base mesh.",
                "Phase 15C locked CR90 production-artwork validation.\n" +
                "This save uses the manually restored First Edition CR90 base texture directly.\n" +
                "The repository-owned First Edition CR90 miniature is mounted at the measured Epic peg-top height.\n" +
                "Verify Fore/Aft orientation, stat bars, firing geometry, turret, action icons, ship silhouette and UV coverage.\n" +
                "The Phase 15C-R15 generated targeting texture is deliberately not used.",
                string.Empty,
                Array.Empty<ProductionPilotCard>());
        }

        if (shipId.Equals(
                "raiderclasscorvette",
                StringComparison.OrdinalIgnoreCase))
        {
            const string textureRepositoryPath =
                "assets/source/unified1e/pilot-tokens/" +
                "galacticempire/raiderclasscorvette/raiderclasscorvette.png";

            return new ProductionValidationProfile(
                "raiderclasscorvette",
                "Raider-class Corvette",
                textureRepositoryPath,
                "AE7DA8CC2D3F70447059DF011C7B109CB3550621D12334BD2F6EFF76A44A08E8",
                Path.Combine(
                    repositoryRoot,
                    textureRepositoryPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "generated",
                    "validation",
                    "epic",
                    "raiderclasscorvette-base-validation.json"),
                "assets/source/unified1e/bases/epic/base.obj",
                "assets/source/unified1e/bases/pegs/epic.obj",
                "assets/source/unified1e/ships/epic/raiderclasscorvette/raider.obj",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "raiderclasscorvette",
                    "raider.obj"),
                "assets/source/unified1e/ships/epic/raiderclasscorvette/Textures/standard.jpg",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "raiderclasscorvette",
                    "Textures",
                    "standard.jpg"),
                "assets/source/unified1e/bases/epic/front/empire.png",
                3.49761486,
                "rdrshp",
                "Repository-owned Raider-class Corvette miniature at the authoritative reference Epic peg-top height.",
                "Raider-class Corvette — Locked First Edition Base",
                "Locked First Edition Raider base-token artwork on the authoritative long Epic base mesh.",
                "Phase 15C locked Raider production-artwork validation.\n" +
                "This save uses the manually restored First Edition Raider base texture directly.\n" +
                "The repository-owned Raider miniature uses the scale, orientation and peg-top height measured from the five-Huge-ship TTS reference save.\n" +
                "Verify Fore/Aft orientation, stat bars, green firing geometry, action icons, ship silhouette, pilot cards and UV coverage.\n" +
                "The Phase 15C-R17 generated targeting texture is deliberately not substituted for the locked production artwork.",
                "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/images/asset__1ca546de0b098648.png",
                new[]
                {
                    new ProductionPilotCard(
                        "rdf001",
                        "Fore",
                        "Raider-class Corv. (Fore)",
                        "assets/source/unified1e/pilot-cards/galacticempire/raiderclasscorvette/raider-class-corv-fore.png",
                        7.5,
                        2.2),
                    new ProductionPilotCard(
                        "rda002",
                        "Aft",
                        "Raider-class Corv. (Aft)",
                        "assets/source/unified1e/pilot-cards/galacticempire/raiderclasscorvette/raider-class-corv-aft.png",
                        7.5,
                        -2.2)
                });
        }

        if (shipId.Equals(
                "gozanticlasscruiser",
                StringComparison.OrdinalIgnoreCase))
        {
            const string textureRepositoryPath =
                "assets/source/unified1e/pilot-tokens/" +
                "galacticempire/gozanticlasscruiser/gozanticlasscruiser.png";

            return new ProductionValidationProfile(
                "gozanticlasscruiser",
                "Gozanti-class Cruiser",
                textureRepositoryPath,
                "7114D9173F0C4328D46CD77F5C26AC0E253EE785DF6A359CB733677673197E52",
                Path.Combine(
                    repositoryRoot,
                    textureRepositoryPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "generated",
                    "validation",
                    "epic",
                    "gozanticlasscruiser-base-validation.json"),
                "assets/source/unified1e/bases/epic/base-short.obj",
                "assets/source/unified1e/bases/pegs/epic-short.obj",
                "assets/source/unified1e/ships/epic/gozanticlasscruiser/gozanti.obj",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "gozanticlasscruiser",
                    "gozanti.obj"),
                "assets/source/unified1e/ships/epic/gozanticlasscruiser/Textures/standard.jpg",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "gozanticlasscruiser",
                    "Textures",
                    "standard.jpg"),
                "assets/source/unified1e/bases/epic/front/empire.png",
                3.49761486,
                "gznshp",
                "Repository-owned Gozanti-class Cruiser miniature at the authoritative reference Epic peg-top height.",
                "Gozanti-class Cruiser — Locked First Edition Base",
                "Locked First Edition Gozanti base-token artwork on the authoritative short Epic base mesh.",
                "Phase 15C locked Gozanti production-artwork validation.\n" +
                "This save uses the manually restored First Edition Gozanti base texture directly.\n" +
                "The short First Edition Epic base and peg meshes preserve the original end sections while reducing the centre span.\n" +
                "The repository-owned Gozanti miniature uses the orientation and peg-top height measured from the five-Huge-ship TTS reference save.\n" +
                "Verify dashboard orientation, stat bar, green Fore firing zone, action icons, ship silhouette, pilot card and UV coverage.\n" +
                "The generated targeting texture is deliberately not substituted for the locked production artwork.",
                "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/images/asset__1ca546de0b098648.png",
                new[]
                {
                    new ProductionPilotCard(
                        "gzn001",
                        "Ship",
                        "Gozanti-class Cruiser",
                        "assets/source/unified1e/pilot-cards/galacticempire/gozanticlasscruiser/gozanti-class-cruiser.png",
                        7.5,
                        0.0)
                });
        }

        if (shipId.Equals(
                "croccruiser",
                StringComparison.OrdinalIgnoreCase))
        {
            const string textureRepositoryPath =
                "assets/source/unified1e/pilot-tokens/" +
                "scumandvillainy/croccruiser/croccruiser.png";

            return new ProductionValidationProfile(
                "croccruiser",
                "C-ROC Cruiser",
                textureRepositoryPath,
                "60BE5F35EC4F6C08118F793BC49646144717FAAA6C61F1538E2982CF93624FC2",
                Path.Combine(
                    repositoryRoot,
                    textureRepositoryPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "generated",
                    "validation",
                    "epic",
                    "croccruiser-base-validation.json"),
                "assets/source/unified1e/bases/epic/base-short.obj",
                "assets/source/unified1e/bases/pegs/epic-short.obj",
                "assets/source/unified1e/ships/epic/croccruiser/croc-donor-ventral-test.obj",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "croccruiser",
                    "croc-donor-ventral-test.obj"),
                "assets/source/unified1e/ships/epic/croccruiser/Textures/standard.jpg",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "croccruiser",
                    "Textures",
                    "standard.jpg"),
                "assets/source/unified1e/bases/epic/front/scum.png",
                3.497615,
                "crcshp",
                "Diagnostic C-ROC Cruiser mesh with donor-assisted ventral bow and aft geometry at the authoritative reference Epic peg-top height.",
                "C-ROC Cruiser — Locked First Edition Base",
                "Locked First Edition C-ROC base-token artwork on the authoritative short Epic base mesh.",
                "Phase 15C locked C-ROC production-artwork validation.\n" +
                "This save uses the manually restored First Edition C-ROC base texture directly.\n" +
                "The physical First Edition reference confirms that the C-ROC has no blue Fore/Aft divider.\n" +
                "This validation uses a diagnostic C-ROC mesh with aligned ventral bow and aft geometry to repair inherited missing surfaces while preserving intentional openings.\n" +
                "The repository-owned C-ROC miniature uses the orientation and peg-top height measured from the five-Huge-ship TTS reference save.\n" +
                "Verify stat bar, yellow Fore firing zone, action icons, ship silhouette, pilot card and UV coverage.",
                "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/images/asset__1ca546de0b098648.png",
                new[]
                {
                    new ProductionPilotCard(
                        "crc001",
                        "Ship",
                        "C-ROC Cruiser",
                        "assets/source/unified1e/pilot-cards/scumandvillainy/croccruiser/c-roc-cruiser.png",
                        7.5,
                        0.0)
                });
        }

        if (shipId.Equals(
                "gr75mediumtransport",
                StringComparison.OrdinalIgnoreCase))
        {
            const string textureRepositoryPath =
                "assets/source/unified1e/pilot-tokens/" +
                "rebelalliance/gr75mediumtransport/gr75mediumtransport.png";

            return new ProductionValidationProfile(
                "gr75mediumtransport",
                "GR-75 Medium Transport",
                textureRepositoryPath,
                "E88268D8B3A1F6E9BD34FDD824616F0C2316E1D922E2108E4798A68CDF238AF1",
                Path.Combine(
                    repositoryRoot,
                    textureRepositoryPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "generated",
                    "validation",
                    "epic",
                    "gr75mediumtransport-base-validation.json"),
                "assets/source/unified1e/bases/epic/base-short.obj",
                "assets/source/unified1e/bases/pegs/epic-short.obj",
                "assets/source/unified1e/ships/epic/gr75mediumtransport/gr75.obj",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "gr75mediumtransport",
                    "gr75.obj"),
                "assets/source/unified1e/ships/epic/gr75mediumtransport/Textures/standard.jpg",
                Path.Combine(
                    repositoryRoot,
                    "assets",
                    "source",
                    "unified1e",
                    "ships",
                    "epic",
                    "gr75mediumtransport",
                    "Textures",
                    "standard.jpg"),
                "assets/source/unified1e/bases/epic/front/rebel.png",
                3.497615,
                "g75shp",
                "Repository-owned GR-75 Medium Transport miniature at the authoritative reference Epic peg-top height.",
                "GR-75 Medium Transport — Locked First Edition Base",
                "Locked First Edition GR-75 base-token artwork on the authoritative short Epic base mesh.",
                "Phase 15C locked GR-75 production-artwork validation.\n" +
                "This save uses the manually restored First Edition GR-75 base texture directly.\n" +
                "The repository-owned GR-75 miniature uses the orientation and peg-top height measured from the five-Huge-ship TTS reference save.\n" +
                "Verify dashboard orientation, stat bar, blue divider, action icons, ship silhouette, pilot card and UV coverage.",
                "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/images/asset__1ca546de0b098648.png",
                new[]
                {
                    new ProductionPilotCard(
                        "g75001",
                        "Ship",
                        "GR-75 Medium Transport",
                        "assets/source/unified1e/pilot-cards/rebelalliance/gr75mediumtransport/gr-75-medium-transport.png",
                        7.5,
                        0.0)
                });
        }

        throw new InvalidDataException(
            $"No locked Epic production texture is registered for '{shipId}'.");
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
        string BaseMeshRepositoryPath,
        string PegMeshRepositoryPath,
        string ShipModelRepositoryPath,
        string ShipModelPath,
        string ShipTextureRepositoryPath,
        string ShipTexturePath,
        string PegTextureRepositoryPath,
        double ShipHeight,
        string ShipObjectGuid,
        string ShipObjectDescription,
        string ObjectNickname,
        string ObjectDescription,
        string Notes,
        string PilotCardBackRepositoryPath,
        IReadOnlyList<ProductionPilotCard> PilotCards);

    private sealed record ProductionPilotCard(
        string Guid,
        string Section,
        string Nickname,
        string RepositoryPath,
        double PositionX,
        double PositionZ);

    private sealed record EpicComparisonPlacement(
        ProductionValidationProfile Profile,
        double PositionX,
        double PositionZ,
        string BaseGuid,
        string PegGuid);
}
