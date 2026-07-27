using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12B-3:
/// Generates the first six-ship structural TTS prototype save.
///
/// This revision validates the complete object graph and asset assignment:
/// base, correct peg type, ship model, pilot token, pilot card and assigned dial.
///
/// It deliberately retains the known-good bundled dial/base Lua from the
/// reference save. The generated First Edition dial source will be bundled into
/// these objects in the following runtime-integration revision.
/// </summary>
public static class GeneratePrototypeSaveCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var referenceSavePath = Path.GetFullPath(args[1]);

            ValidateFile(referenceSavePath, "Reference TTS save");

            var assemblyPlanPath = ResolvePath(
                repositoryRoot,
                args,
                "--assembly-plan",
                "_unifiedtoolkit_reports/phase12b/five-ship-prototype-assembly/five-ship-prototype-assembly-plan.json");

            var runtimeTemplatesPath = ResolvePath(
                repositoryRoot,
                args,
                "--runtime-templates",
                "_unifiedtoolkit_reports/phase12b/runtime-template-extraction/runtime-templates.json");

            ValidateFile(assemblyPlanPath, "Phase 12B prototype assembly plan");
            ValidateFile(runtimeTemplatesPath, "Phase 12B runtime-template manifest");

            var assetBaseUrl = ResolveAssetBaseUrl(args);
            var outputPath = ResolveOutputPath(repositoryRoot, args);

            var referenceSave = JsonNode.Parse(
                    File.ReadAllText(referenceSavePath))?.AsObject()
                ?? throw new InvalidDataException(
                    "Could not parse the reference TTS save.");

            var assemblyPlan = Read<PrototypeSaveAssemblyPlanInput>(
                assemblyPlanPath);
            var runtimeTemplates = Read<PrototypeRuntimeTemplateManifestInput>(
                runtimeTemplatesPath);
            var dialRuntime = LoadFirstEditionDialRuntime(repositoryRoot);

            if (assemblyPlan.InvalidPrototypeCount != 0
                || assemblyPlan.ValidationErrors.Count != 0)
            {
                throw new InvalidDataException(
                    "The prototype assembly plan is not fully valid.");
            }

            var baseTemplates = LoadRequiredSnapshots(
                runtimeTemplates,
                new[]
                {
                    "FirstEditionSmallShipBase",
                    "FirstEditionLargeShipBase",
                    "FirstEditionAssignedDial"
                });

            var pegIndex = runtimeTemplates.Templates
                .Where(template =>
                    template.TemplateKey.EndsWith(
                        "ShipPeg",
                        StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    template => template.TemplateKey,
                    template => template,
                    StringComparer.OrdinalIgnoreCase);

            var diagnostics = new List<string>();
            var generatedObjects = new JsonArray();
            var usedGuids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in assemblyPlan.Assemblies)
            {
                var shipObjects = BuildAssemblyObjects(
                    repositoryRoot,
                    assetBaseUrl,
                    assembly,
                    baseTemplates,
                    pegIndex,
                    dialRuntime,
                    usedGuids,
                    diagnostics);

                foreach (var item in shipObjects)
                    generatedObjects.Add(item);
            }

            var outputSave = referenceSave.DeepClone().AsObject();
            outputSave["SaveName"] =
                "X-Wing Unified 1E - Phase 12E R1 Runtime Faction Dial Prototype";
            outputSave["Date"] = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // The full Unified Global runtime expects the complete original
            // table and command infrastructure. This isolated visual prototype
            // intentionally disables it, preventing the repeating
            // "pattern too complex" command-parser error.
            outputSave["LuaScript"] = string.Empty;
            outputSave["LuaScriptState"] = string.Empty;
            outputSave["XmlUI"] = string.Empty;
            outputSave["ObjectStates"] = generatedObjects;

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            File.WriteAllText(
                outputPath,
                outputSave.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
                new UTF8Encoding(false));

            var reportDirectory = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12b",
                "prototype-save-generation");
            Directory.CreateDirectory(reportDirectory);

            var manifest = new PrototypeSaveGenerationManifest
            {
                SchemaVersion = "1.0.0",
                ImplementationVersion = "12E-5A-R1",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                ReferenceSave = NormalisePath(referenceSavePath),
                AssemblyPlanPath = NormalisePath(assemblyPlanPath),
                RuntimeTemplatesPath = NormalisePath(runtimeTemplatesPath),
                OutputSave = NormalisePath(outputPath),
                AssetBaseUrl = assetBaseUrl,
                AssembliesGenerated = assemblyPlan.Assemblies.Count,
                TtsObjectsGenerated = generatedObjects.Count,
                Diagnostics = diagnostics,
                RuntimeMode = "AssignedUnifiedDial-RepositoryOwnedAlignedModel-R1"
            };

            var manifestPath = Path.Combine(
                reportDirectory,
                "prototype-save-generation.json");
            var reportPath = Path.Combine(
                reportDirectory,
                "PROTOTYPE-SAVE-GENERATION.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteReport(reportPath, manifest, assemblyPlan.Assemblies);

            Console.WriteLine(
                "UnifiedToolkit Phase 12B-3 Structural Prototype Save Generation");
            Console.WriteLine(
                "=================================================================");
            Console.WriteLine("Implementation:          12E-5C R1 Dial Front Alignment and Centring");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Reference save:          {referenceSavePath}");
            Console.WriteLine($"Assembly plan:           {assemblyPlanPath}");
            Console.WriteLine($"Runtime templates:       {runtimeTemplatesPath}");
            Console.WriteLine($"Asset base URL:          {assetBaseUrl}");
            Console.WriteLine();
            Console.WriteLine($"Assemblies generated:    {manifest.AssembliesGenerated}");
            Console.WriteLine($"TTS objects generated:   {manifest.TtsObjectsGenerated}");
            Console.WriteLine($"Diagnostics:             {manifest.Diagnostics.Count}");
            Console.WriteLine();
            Console.WriteLine($"Prototype save:          {outputPath}");
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Structural prototype generated. The reference save and repository assets were not modified.");

            return diagnostics.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Prototype-save generation failed: {ex.Message}");
            return 1;
        }
    }

    private static List<JsonObject> BuildAssemblyObjects(
        string repositoryRoot,
        string assetBaseUrl,
        PrototypeAssemblyInput assembly,
        IReadOnlyDictionary<string, JsonObject> snapshots,
        IReadOnlyDictionary<string, PrototypeRuntimeTemplateInput> pegIndex,
        FirstEditionDialRuntimeInput dialRuntime,
        ISet<string> usedGuids,
        ICollection<string> diagnostics)
    {
        var baseTemplate = snapshots[assembly.BaseTemplateKey]
            .DeepClone()
            .AsObject();

        var dialTemplate = snapshots["FirstEditionAssignedDial"]
            .DeepClone()
            .AsObject();

        var baseGuid = NextGuid(
            assembly.PackageId + ":base",
            usedGuids);
        var dialGuid = NextGuid(
            assembly.PackageId + ":dial",
            usedGuids);
        var pegGuid = NextGuid(
            assembly.PackageId + ":peg",
            usedGuids);
        var shipGuid = NextGuid(
            assembly.PackageId + ":ship",
            usedGuids);
        var cardGuid = NextGuid(
            assembly.PackageId + ":card",
            usedGuids);
        var tokenGuid = NextGuid(
            assembly.PackageId + ":pilot-token",
            usedGuids);
        var openConfigGuid = NextGuid(
            assembly.PackageId + ":config-open",
            usedGuids);
        var closedConfigGuid = NextGuid(
            assembly.PackageId + ":config-closed",
            usedGuids);

        var model = RequireAsset(assembly, "ShipModel");
        var texture = RequireAsset(assembly, "ShipTexture");
        var pilotToken = RequireAsset(assembly, "PilotBaseToken");
        var pilotCard = RequireAsset(assembly, "PilotCard");
        var dialTexture = RequireAsset(assembly, "DialTexture");

        model = CorrectKnownPrototypeShipModel(
            repositoryRoot,
            assembly,
            model,
            diagnostics);

        model = CorrectXWingMultipartModel(
            repositoryRoot,
            assembly,
            model,
            diagnostics);

        model = CorrectMisclassifiedBwingModel(
            repositoryRoot,
            assembly,
            model,
            diagnostics);

        if (!pegIndex.TryGetValue(
                assembly.PegTemplateKey,
                out var pegTemplate))
        {
            throw new InvalidDataException(
                $"Peg template '{assembly.PegTemplateKey}' is missing.");
        }

        texture = CorrectPrototypeShipTexture(
            repositoryRoot,
            assembly,
            texture,
            diagnostics);

        texture = PublishPrototypeTexture(
            repositoryRoot,
            assembly,
            texture,
            diagnostics);

        var modelUrl = AssetUrl(assetBaseUrl, model.RepositoryPath);
        var textureUrl = AssetUrl(assetBaseUrl, texture.RepositoryPath);
        var tokenUrl = AssetUrl(assetBaseUrl, pilotToken.RepositoryPath);
        var cardUrl = AssetUrl(assetBaseUrl, pilotCard.RepositoryPath);

        ValidateFile(
            dialTexture.FullPath,
            $"{assembly.Faction} faction master dial texture");

        var dialUrl = AssetUrl(assetBaseUrl, dialTexture.RepositoryPath);

        diagnostics.Add(
            $"{assembly.PilotName} dial uses the shared {assembly.Faction} " +
            $"master texture directly: {dialTexture.RepositoryPath}.");

        var pegUrl = pegTemplate.AssetUrl.Length > 0
            ? pegTemplate.AssetUrl
            : AssetUrl(assetBaseUrl, pegTemplate.RepositoryPath);

        ConfigureBase(
            baseTemplate,
            baseGuid,
            assembly,
            tokenUrl,
            modelUrl,
            textureUrl,
            pegUrl,
            pegGuid,
            shipGuid,
            tokenGuid,
            openConfigGuid,
            closedConfigGuid,
            assetBaseUrl,
            repositoryRoot,
            diagnostics);

        ConfigureDial(
            dialTemplate,
            dialGuid,
            baseGuid,
            assembly,
            dialUrl,
            dialRuntime,
            assetBaseUrl);

        var cardObject = BuildPilotCard(
            repositoryRoot,
            assetBaseUrl,
            cardGuid,
            assembly,
            cardUrl);

        return new List<JsonObject>
        {
            baseTemplate,
            dialTemplate,
            cardObject
        };
    }

    private static void ConfigureBase(
        JsonObject baseObject,
        string baseGuid,
        PrototypeAssemblyInput assembly,
        string tokenUrl,
        string modelUrl,
        string textureUrl,
        string pegUrl,
        string pegGuid,
        string shipGuid,
        string tokenGuid,
        string openConfigGuid,
        string closedConfigGuid,
        string assetBaseUrl,
        string repositoryRoot,
        ICollection<string> diagnostics)
    {
        baseObject["GUID"] = baseGuid;
        baseObject["Nickname"] = $"{assembly.PilotName} — {assembly.ShipName}";
        baseObject["Description"] =
            $"Phase 12B structural prototype\n" +
            $"Ship: {assembly.ShipName}\n" +
            $"Pilot: {assembly.PilotName}\n" +
            $"Base: {assembly.BaseTemplateKey}\n" +
            $"Peg: {assembly.PegTemplateKey}";

        baseObject["Tooltip"] = true;
        baseObject["Locked"] = false;

        // TS_Save_42 confirms that both standard Small and Large spawned
        // base objects use the same parent scale and resting height.
        SetTransform(
            baseObject,
            assembly.PositionX,
            1.10336781f,
            assembly.PositionZ,
            0.629f);

        var customMesh = EnsureObject(
            baseObject,
            "CustomMesh");
        customMesh["DiffuseURL"] = PublishFactionBaseTexture(
            repositoryRoot,
            assetBaseUrl,
            assembly,
            diagnostics);

        var childObjects = new JsonArray
        {
            BuildPilotTokenChild(
                tokenGuid,
                tokenUrl,
                assembly),
            BuildPegChild(
                pegGuid,
                pegUrl,
                assembly.BaseSize),
            BuildShipChild(
                shipGuid,
                modelUrl,
                textureUrl,
                assembly)
        };

        AddMultipartModelStates(
            childObjects,
            openConfigGuid,
            closedConfigGuid,
            modelUrl,
            textureUrl,
            assembly,
            assetBaseUrl);

        baseObject["ChildObjects"] = childObjects;
        baseObject.Remove("ContainedObjects");

        var state = new JsonObject
        {
            ["arcIndicators"] = new JsonArray(),
            ["finishedSetup"] = true,
            ["interactable"] = true,
            ["owningPlayer"] = "Black",
            ["shipData"] = new JsonObject
            {
                ["actSet"] = ToJsonArray(assembly.ActSet),
                ["executeOptions"] = new JsonArray(),
                ["initiative"] = 0,
                ["mesh"] = modelUrl,
                ["mountingPoints"] = new JsonObject
                {
                    ["main"] = new JsonArray(0, 0)
                },
                ["moveSet"] = ToJsonArray(assembly.MoveSet),
                ["movethrough"] = false,
                ["ProximityHider"] = false,
                ["shipId"] = assembly.ShipId,
                ["Size"] = assembly.BaseSize,
                ["texture"] = "standard",
                ["textures"] = new JsonObject
                {
                    ["standard"] = textureUrl
                },
                ["firstEditionActions"] =
                    ToJsonArray(assembly.FirstEditionActions)
            },
            ["tokenData"] = new JsonObject
            {
                ["tokens"] = new JsonArray()
            },
            ["uiData"] = new JsonObject
            {
                ["icon"] = assembly.ShipId,
                ["init"] = "init0",
                ["name"] = assembly.PilotName
            }
        };

        // R2 is a static visual prototype. Disable the cloned base runtime,
        // which otherwise creates pilot-name buttons and calls unavailable
        // Global functions. The validated semantic state is retained in GMNotes
        // for inspection and will be re-bundled with the First Edition runtime
        // in a later revision.
        baseObject["GMNotes"] = state.ToJsonString();
        baseObject["LuaScript"] = string.Empty;
        baseObject["LuaScriptState"] = string.Empty;
        baseObject["XmlUI"] = string.Empty;
        baseObject["CustomUIAssets"] = new JsonArray();
    }

    private static JsonObject BuildPilotTokenChild(
        string guid,
        string tokenUrl,
        PrototypeAssemblyInput assembly)
    {
        var scale = assembly.BaseSize.Equals(
            "large",
            StringComparison.OrdinalIgnoreCase)
            ? 2.0
            : 1.0;

        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Tile",
            ["Transform"] = new JsonObject
            {
                ["posX"] = 0.0,
                ["posY"] = 0.24,
                ["posZ"] = 0.0,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = scale,
                ["scaleY"] = 1.0,
                ["scaleZ"] = scale
            },
            ["Nickname"] = string.Empty,
            ["Description"] = string.Empty,
            ["ColorDiffuse"] = OpaqueWhite(),
            ["Locked"] = true,
            ["Grid"] = false,
            ["Snap"] = false,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = false,
            ["Autoraise"] = false,
            ["Sticky"] = true,
            ["Tooltip"] = false,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomImage"] = new JsonObject
            {
                ["ImageURL"] = tokenUrl,
                ["ImageSecondaryURL"] = tokenUrl,
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
    }

    private static void AddMultipartModelStates(
        JsonArray childObjects,
        string openGuid,
        string closedGuid,
        string baseModelUrl,
        string textureUrl,
        PrototypeAssemblyInput assembly,
        string assetBaseUrl)
    {
        string? openPath = null;
        string? closedPath = null;

        if (assembly.ShipId.Equals("xwing", StringComparison.OrdinalIgnoreCase)
            || assembly.ShipId.Equals("t65xwing", StringComparison.OrdinalIgnoreCase))
        {
            openPath =
                "assets/source/unified25/assets/ships-v2/small/t65xwing/xwingopenv3.obj";
            closedPath =
                "assets/source/unified25/assets/ships-v2/small/t65xwing/xwingclosedv3.obj";
        }
        else if (assembly.PegTemplateKey.Equals(
                     "FirstEditionBwingShipPeg",
                     StringComparison.OrdinalIgnoreCase))
        {
            openPath =
                "assets/source/unified25/assets/ships-v2/small/asf01bwing/bwing-open.obj";
            closedPath =
                "assets/source/unified25/assets/ships-v2/small/asf01bwing/bwing-closed.obj";
        }

        if (openPath is null || closedPath is null)
            return;

        childObjects.Add(BuildConfigChild(
            openGuid,
            AssetUrl(assetBaseUrl, openPath),
            textureUrl,
            visible: true));

        childObjects.Add(BuildConfigChild(
            closedGuid,
            AssetUrl(assetBaseUrl, closedPath),
            textureUrl,
            visible: false));
    }

    private static JsonObject BuildConfigChild(
        string guid,
        string modelUrl,
        string textureUrl,
        bool visible)
    {
        var scale = visible ? 1.0 : 0.000158982511;

        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Model",
            ["Transform"] = new JsonObject
            {
                ["posX"] = -1.18049023e-07,
                ["posY"] = 3.497615,
                ["posZ"] = 6.2130125e-15,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = scale,
                ["scaleY"] = visible ? 0.99999994 : 0.0001589825,
                ["scaleZ"] = scale
            },
            ["Nickname"] = "Config",
            ["Description"] = string.Empty,
            ["ColorDiffuse"] = OpaqueWhite(),
            ["Locked"] = false,
            ["Grid"] = true,
            ["Snap"] = true,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = true,
            ["Autoraise"] = true,
            ["Sticky"] = true,
            ["Tooltip"] = false,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomMesh"] = new JsonObject
            {
                ["MeshURL"] = modelUrl,
                ["DiffuseURL"] = textureUrl,
                ["NormalURL"] = string.Empty,
                ["ColliderURL"] =
                    "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/colliders/Small_base_Collider.obj",
                ["Convex"] = true,
                ["MaterialIndex"] = 1,
                ["TypeIndex"] = 1,
                ["CastShadows"] = true
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static JsonObject BuildStaticDialToken(
        string dialGuid,
        PrototypeAssemblyInput assembly,
        string dialTextureUrl,
        string dialBackUrl)
    {
        return new JsonObject
        {
            ["GUID"] = dialGuid,
            ["Name"] = "Custom_Token",
            ["Transform"] = new JsonObject
            {
                ["posX"] = assembly.PositionX,
                ["posY"] = 1.0,
                ["posZ"] = assembly.PositionZ + 5.5,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 0.70,
                ["scaleY"] = 1.0,
                ["scaleZ"] = 0.70
            },
            ["Nickname"] = string.Empty,
            ["Description"] = $"{assembly.ShipName} First Edition dial artwork",
            ["ColorDiffuse"] = OpaqueWhite(),
            ["Locked"] = false,
            ["Grid"] = true,
            ["Snap"] = true,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = true,
            ["Autoraise"] = true,
            ["Sticky"] = true,
            ["Tooltip"] = false,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomImage"] = new JsonObject
            {
                ["ImageURL"] = dialTextureUrl,
                ["ImageSecondaryURL"] = dialBackUrl,
                ["ImageScalar"] = 1.0,
                ["WidthScale"] = 0.0,
                ["CustomToken"] = new JsonObject
                {
                    ["Thickness"] = 0.10,
                    ["MergeDistancePixels"] = 5.0,
                    ["StandUp"] = false,
                    ["Stackable"] = false
                }
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static string ResolveNeutralBaseTexture(
        PrototypeAssemblyInput assembly,
        string assetBaseUrl)
    {
        var factionFile = assembly.Faction.ToLowerInvariant() switch
        {
            "galacticempire" => "empire.png",
            "firstorder" => "firstorder.png",
            "scumandvillainy" => "scum.png",
            _ => "rebel.png"
        };

        var baseFolder = assembly.BaseSize.Equals(
            "large",
            StringComparison.OrdinalIgnoreCase)
            ? "large"
            : "small";

        return
            "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/" +
            $"assets/ships-v2/bases/{baseFolder}/front/{factionFile}";
    }

    private static string ResolvePilotCardBackUrl(
        string repositoryRoot,
        string assetBaseUrl,
        string faction)
    {
        var relativePath = faction.ToLowerInvariant() switch
        {
            "firstorder" =>
                "assets/source/legacy1e-non-pilot/" +
                "steamusercontent-a.akamaihd.net/other/" +
                "asset__5116e93fe5cbf393.png",

            "galacticempire" =>
                "assets/source/legacy1e-non-pilot/" +
                "steamusercontent-a.akamaihd.net/images/" +
                "asset__1ca546de0b098648.png",

            "rebelalliance" =>
                "assets/source/legacy1e-non-pilot/" +
                "steamusercontent-a.akamaihd.net/images/" +
                "asset__08124f6a69f3b2a3.png",

            "resistance" =>
                "assets/source/legacy1e-non-pilot/" +
                "steamusercontent-a.akamaihd.net/images/" +
                "asset__2d891250284c8a5b.jpg",

            "scumandvillainy" =>
                "assets/source/legacy1e-non-pilot/" +
                "steamusercontent-a.akamaihd.net/images/" +
                "asset__54724d9056f5f36a.png",

            _ => throw new InvalidDataException(
                $"No First Edition pilot-card back is registered for faction '{faction}'.")
        };

        var fullPath = Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        ValidateFile(
            fullPath,
            $"{faction} First Edition pilot-card back");

        return AssetUrl(assetBaseUrl, relativePath);
    }

    private static JsonObject BuildPegChild(
        string guid,
        string pegUrl,
        string baseSize)
    {
        return new JsonObject
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
                ["scaleY"] = 0.99999994,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = "Peg",
            ["Description"] = string.Empty,
            ["ColorDiffuse"] = VisiblePegWhite(),
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
            ["Hands"] = false,
            ["CustomMesh"] = new JsonObject
            {
                ["MeshURL"] = pegUrl,
                ["DiffuseURL"] = string.Empty,
                ["NormalURL"] = string.Empty,
                ["ColliderURL"] =
                    "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/models/minisculebox.obj",
                ["Convex"] = true,
                ["MaterialIndex"] = 1,
                ["TypeIndex"] = 1,
                ["CastShadows"] = true
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static JsonObject BuildShipChild(
        string guid,
        string modelUrl,
        string textureUrl,
        PrototypeAssemblyInput assembly)
    {
        // The Unified base/peg geometry is authored around this exact
        // mounting height for both Small and Large standard ships.
        const double height = 3.49761486;

        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Model",
            ["Transform"] = new JsonObject
            {
                ["posX"] = -1.18049023e-07,
                ["posY"] = height,
                ["posZ"] = 6.2130125e-15,
                ["rotX"] = 0.0,
                ["rotY"] = 0.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 1.0,
                ["scaleY"] = 0.99999994,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = $"{assembly.PilotName} — {assembly.ShipName}",
            ["Description"] = assembly.ShipName,
            ["ColorDiffuse"] = OpaqueWhite(),
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
            ["Hands"] = false,
            ["CustomMesh"] = new JsonObject
            {
                ["MeshURL"] = modelUrl,
                ["DiffuseURL"] = textureUrl,
                ["NormalURL"] = string.Empty,
                ["ColliderURL"] =
                    "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/models/minisculebox.obj",
                ["Convex"] = true,
                ["MaterialIndex"] = 1,
                ["TypeIndex"] = 1,
                ["CustomShader"] = new JsonObject
                {
                    ["SpecularColor"] = new JsonObject
                    {
                        ["r"] = 0.875,
                        ["g"] = 0.813,
                        ["b"] = 0.746
                    },
                    ["SpecularIntensity"] = 0.05,
                    ["SpecularSharpness"] = 4.0,
                    ["FresnelStrength"] = 0.1
                },
                ["CastShadows"] = true
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static void ConfigureDial(
        JsonObject dialObject,
        string dialGuid,
        string baseGuid,
        PrototypeAssemblyInput assembly,
        string dialTextureUrl,
        FirstEditionDialRuntimeInput dialRuntime,
        string assetBaseUrl)
    {
        dialObject["GUID"] = dialGuid;
        dialObject["Name"] = "Custom_Model";
        dialObject["Nickname"] = assembly.PilotName;
        dialObject["Description"] =
            $"{assembly.ShipName} First Edition manoeuvre dial";
        dialObject["Locked"] = false;
        dialObject["Grid"] = true;
        dialObject["Snap"] = true;
        dialObject["DragSelectable"] = true;
        dialObject["Autoraise"] = true;
        dialObject["Sticky"] = true;
        dialObject["Tooltip"] = true;
        dialObject["Hands"] = false;

        SetTransform(
            dialObject,
            assembly.PositionX,
            1.0f,
            assembly.PositionZ + 5.5f,
            0.70f);

        var transform = dialObject["Transform"]?.AsObject()
            ?? throw new InvalidDataException(
                "Assigned dial template has no Transform object.");
        transform["rotX"] = 0.0;
        transform["rotY"] = 0.0;
        transform["rotZ"] = 0.0;
        transform["scaleX"] = 0.70;
        transform["scaleY"] = 0.70;
        transform["scaleZ"] = 0.70;

        var customMesh = dialObject["CustomMesh"]?.AsObject()
            ?? throw new InvalidDataException(
                "Assigned dial template has no CustomMesh object.");
        customMesh["MeshURL"] = AssetUrl(
            assetBaseUrl,
            "assets/generated/FirstEditionDialModel/first-edition-dial-model-uv-plus-5-v-plus-0_02.obj");
        customMesh["DiffuseURL"] = dialTextureUrl;

        var bundledLua = dialObject["LuaScript"]?.GetValue<string>()
            ?? throw new InvalidDataException(
                "Assigned dial template has no bundled Lua runtime.");

        dialObject["LuaScript"] = ReplaceBundledDialModules(
            bundledLua,
            dialRuntime.Modules);
        dialObject["XmlUI"] = dialRuntime.Xml;
        dialObject["CustomUIAssets"] = MergeDialUiAssets(
            dialObject["CustomUIAssets"] as JsonArray,
            dialRuntime.Assets);

        var dialState = new JsonObject
        {
            ["assignedShipGUID"] = baseGuid,
            ["clickMode"] = true,
            ["owningPlayer"] = "Black",
            ["owningPlayerTeam"] = "",
            ["proxyMode"] = true,
            ["timeoutDuration"] = 20
        };

        var encodedState = dialState.ToJsonString();
        dialObject["LuaScriptState"] = encodedState;
        dialObject["GMNotes"] = encodedState;
    }

    private static string ReplaceBundledDialModules(
        string bundledLua,
        IReadOnlyDictionary<string, string> modules)
    {
        var result = bundledLua;

        foreach (var module in modules)
        {
            var registration =
                $"__bundle_register(\"{module.Key}\", function(require, _LOADED, __bundle_register, __bundle_modules)";
            var registrationIndex = result.IndexOf(
                registration,
                StringComparison.Ordinal);

            if (registrationIndex < 0)
            {
                throw new InvalidDataException(
                    $"Assigned dial bundle does not contain module '{module.Key}'.");
            }

            var bodyStart = result.IndexOf('\n', registrationIndex);
            if (bodyStart < 0)
            {
                throw new InvalidDataException(
                    $"Could not locate the body of bundled module '{module.Key}'.");
            }
            bodyStart += 1;

            var bodyEnd = result.IndexOf(
                "\nend)\n__bundle_register(\"",
                bodyStart,
                StringComparison.Ordinal);

            if (bodyEnd < 0)
            {
                bodyEnd = result.IndexOf(
                    "\nend)\nreturn __bundle_require",
                    bodyStart,
                    StringComparison.Ordinal);
            }

            if (bodyEnd < 0)
            {
                throw new InvalidDataException(
                    $"Could not locate the end of bundled module '{module.Key}'.");
            }

            var normalisedSource = module.Value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .TrimEnd();

            result = result[..bodyStart]
                + normalisedSource
                + result[bodyEnd..];
        }

        return result;
    }

    private static JsonArray MergeDialUiAssets(
        JsonArray? templateAssets,
        IReadOnlyList<FirstEditionDialRuntimeAssetInput> runtimeAssets)
    {
        var merged = templateAssets?.DeepClone().AsArray()
            ?? new JsonArray();

        var byName = new Dictionary<string, JsonObject>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in merged)
        {
            if (node is not JsonObject asset)
                continue;

            var name = asset["Name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name))
                byName[name] = asset;
        }

        foreach (var runtimeAsset in runtimeAssets)
        {
            if (byName.TryGetValue(runtimeAsset.LogicalName, out var existing))
            {
                existing["Type"] = 0;
                existing["URL"] = runtimeAsset.Url;
                continue;
            }

            var added = new JsonObject
            {
                ["Type"] = 0,
                ["Name"] = runtimeAsset.LogicalName,
                ["URL"] = runtimeAsset.Url
            };
            merged.Add(added);
            byName[runtimeAsset.LogicalName] = added;
        }

        return merged;
    }

    private static FirstEditionDialRuntimeInput LoadFirstEditionDialRuntime(
        string repositoryRoot)
    {
        var runtimeRoot = Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "FirstEditionDialRuntime",
            "Dial");

        var manifestPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase12a",
            "dial-runtime-integration",
            "first-edition-dial-runtime.json");

        ValidateFile(manifestPath, "First Edition dial-runtime manifest");

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the First Edition dial-runtime manifest.");

        var assets = new List<FirstEditionDialRuntimeAssetInput>();
        if (manifest["assets"] is not JsonArray assetArray)
        {
            throw new InvalidDataException(
                "First Edition dial-runtime manifest has no assets array.");
        }

        foreach (var node in assetArray)
        {
            if (node is not JsonObject asset)
                continue;

            var logicalName = asset["logicalName"]?.GetValue<string>() ?? "";
            var url = asset["url"]?.GetValue<string>() ?? "";
            if (logicalName.Length == 0 || url.Length == 0)
            {
                throw new InvalidDataException(
                    "First Edition dial-runtime manifest contains an incomplete logical asset.");
            }

            assets.Add(new FirstEditionDialRuntimeAssetInput
            {
                LogicalName = logicalName,
                Url = url
            });
        }

        var moduleFiles = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["Dial.UnassignedDial"] = "UnassignedDial.lua",
            ["Dial.Proxy"] = "Proxy.lua",
            ["Dial.Button"] = "Button.lua",
            ["Dial.Menu"] = "Menu.lua"
        };

        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var moduleFile in moduleFiles)
        {
            var path = Path.Combine(runtimeRoot, moduleFile.Value);
            ValidateFile(path, $"Generated dial module '{moduleFile.Key}'");
            modules[moduleFile.Key] = File.ReadAllText(path);
        }

        var xmlPath = Path.Combine(runtimeRoot, "UnassignedDial.xml");
        ValidateFile(xmlPath, "Generated UnassignedDial.xml");

        return new FirstEditionDialRuntimeInput
        {
            Xml = File.ReadAllText(xmlPath),
            Modules = modules,
            Assets = assets
        };
    }

    private static JsonObject BuildPilotCard(
        string repositoryRoot,
        string assetBaseUrl,
        string guid,
        PrototypeAssemblyInput assembly,
        string cardUrl)
    {
        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Tile",
            ["Transform"] = new JsonObject
            {
                ["posX"] = assembly.PositionX,
                ["posY"] = 1.0,
                ["posZ"] = assembly.PositionZ - 6.0,
                ["rotX"] = 0.0,
                ["rotY"] = 180.0,
                ["rotZ"] = 0.0,
                ["scaleX"] = 1.0,
                ["scaleY"] = 1.0,
                ["scaleZ"] = 1.0
            },
            ["Nickname"] = assembly.PilotName,
            ["Description"] = assembly.ShipName,
            ["ColorDiffuse"] = OpaqueWhite(),
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
                ["ImageURL"] = cardUrl,
                ["ImageSecondaryURL"] = ResolvePilotCardBackUrl(
                    repositoryRoot,
                    assetBaseUrl,
                    assembly.Faction),
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
    }

    private static PrototypeAssetInput UseKnownTexture(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput current,
        string relativePath,
        string sourceDescription,
        ICollection<string> diagnostics)
    {
        var fullPath = Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        ValidateFile(fullPath, $"{assembly.ShipName} reference texture");

        if (!current.RepositoryPath.Equals(
                relativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                $"{assembly.ShipName} ShipTexture corrected using " +
                $"{sourceDescription}: {current.RepositoryPath} -> " +
                $"{relativePath}");
        }

        return new PrototypeAssetInput
        {
            Role = current.Role,
            AssetId = current.AssetId,
            RepositoryPath = relativePath,
            FullPath = fullPath,
            Exists = true
        };
    }

    private static string PublishFactionDialBack(
        string repositoryRoot,
        string assetBaseUrl,
        PrototypeAssemblyInput assembly,
        ICollection<string> diagnostics)
    {
        var factionId = NormaliseIdentifier(assembly.Faction);

        var colour = factionId switch
        {
            "galacticempire" => new SKColor(50, 58, 66, 255),
            "firstorder" => new SKColor(43, 43, 48, 255),
            "scumandvillainy" => new SKColor(96, 76, 34, 255),
            "resistance" => new SKColor(116, 73, 38, 255),
            _ => new SKColor(92, 43, 48, 255)
        };

        const int size = 512;
        var destination = Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "FirstEditionDialBack",
            factionId,
            "dial-back.png");

        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        using (var bitmap = new SKBitmap(
                   new SKImageInfo(
                       size,
                       size,
                       SKColorType.Rgba8888,
                       SKAlphaType.Premul)))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = colour,
                Style = SKPaintStyle.Fill
            };

            using var rimPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(
                    (byte)Math.Max(0, colour.Red - 18),
                    (byte)Math.Max(0, colour.Green - 18),
                    (byte)Math.Max(0, colour.Blue - 18),
                    255),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 18
            };

            var centre = size / 2f;
            var radius = size / 2f - 10f;

            canvas.DrawCircle(centre, centre, radius, fillPaint);
            canvas.DrawCircle(centre, centre, radius - 8f, rimPaint);

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(destination);
            encoded.SaveTo(stream);
        }

        var relative = NormalisePath(
            Path.GetRelativePath(repositoryRoot, destination));

        diagnostics.Add(
            $"{assembly.Faction} faction-colour First Edition dial back " +
            $"published: {relative}. Commit and push this generated file " +
            "before loading the R6 save.");

        return AssetUrl(assetBaseUrl, relative);
    }

    private static string PublishFactionBaseTexture(
        string repositoryRoot,
        string assetBaseUrl,
        PrototypeAssemblyInput assembly,
        ICollection<string> diagnostics)
    {
        var factionId = NormaliseIdentifier(assembly.Faction);

        var colour = factionId switch
        {
            "galacticempire" => new SKColor(38, 67, 91, 255),
            "firstorder" => new SKColor(62, 62, 68, 255),
            "scumandvillainy" => new SKColor(105, 84, 24, 255),
            "resistance" => new SKColor(125, 76, 25, 255),
            _ => new SKColor(105, 24, 30, 255)
        };

        var destination = Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "PrototypeBaseTexture",
            factionId,
            "plain-faction.png");

        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        using (var bitmap = new SKBitmap(
                   new SKImageInfo(
                       16,
                       16,
                       SKColorType.Rgba8888,
                       SKAlphaType.Premul)))
        {
            bitmap.Erase(colour);

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(
                SKEncodedImageFormat.Png,
                100);
            using var stream = File.Create(destination);
            encoded.SaveTo(stream);
        }

        var relative = NormalisePath(
            Path.GetRelativePath(
                repositoryRoot,
                destination));

        diagnostics.Add(
            $"{assembly.Faction} plain faction-colour base texture published: " +
            $"{relative}. Commit and push this generated file before loading " +
            "the updated R5 save.");

        return AssetUrl(assetBaseUrl, relative);
    }


    private static string NormaliseIdentifier(string value) =>
        new((value ?? string.Empty)
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static PrototypeAssetInput PublishPrototypeTexture(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput texture,
        ICollection<string> diagnostics)
    {
        if (!assembly.ShipId.Equals(
                "tiereaper",
                StringComparison.OrdinalIgnoreCase))
        {
            return texture;
        }

        var extension = Path.GetExtension(texture.FullPath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".jpg";

        var destination = Path.Combine(
            repositoryRoot,
            "assets",
            "generated",
            "PrototypeShipTexture",
            "galacticempire",
            "tiereaper",
            "standard" + extension.ToLowerInvariant());

        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);
        File.Copy(texture.FullPath, destination, true);

        var relative = NormalisePath(
            Path.GetRelativePath(repositoryRoot, destination));

        diagnostics.Add(
            $"TIE Reaper texture published to stable prototype asset: " +
            $"{relative}. Commit and push this generated file before loading " +
            "the R3 save in TTS.");

        return new PrototypeAssetInput
        {
            Role = texture.Role,
            AssetId = texture.AssetId,
            RepositoryPath = relative,
            FullPath = destination,
            Exists = true
        };
    }

    private static PrototypeAssetInput CorrectKnownPrototypeShipModel(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput model,
        ICollection<string> diagnostics)
    {
        string? relative = null;

        if (assembly.ShipId.Equals(
                "kwing",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/medium/" +
                "btls8kwing/kwing.obj";
        }
        else if (assembly.ShipId.Equals(
                     "sheathipedeclassshuttle",
                     StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/small/" +
                "sheathipedeclassshuttle/Sheathipede2.obj";
        }

        if (relative is null)
            return model;

        var fullPath = Path.Combine(
            repositoryRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        ValidateFile(fullPath, $"{assembly.ShipName} reference model");

        if (!model.RepositoryPath.Equals(
                relative,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                $"{assembly.ShipName} ShipModel corrected from TS_Save_43: " +
                $"{model.RepositoryPath} -> {relative}");
        }

        return new PrototypeAssetInput
        {
            Role = model.Role,
            AssetId = model.AssetId,
            RepositoryPath = relative,
            FullPath = fullPath,
            Exists = true
        };
    }

    private static PrototypeAssetInput CorrectPrototypeShipTexture(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput texture,
        ICollection<string> diagnostics)
    {
        if (assembly.ShipId.Equals(
                "kwing",
                StringComparison.OrdinalIgnoreCase))
        {
            return UseKnownTexture(
                repositoryRoot,
                assembly,
                texture,
                "assets/source/unified25/assets/ships-v2/medium/" +
                "btls8kwing/Textures/MirandaDoni.png",
                "TS_Save_43 red/white default",
                diagnostics);
        }

        if (assembly.ShipId.Equals(
                "sheathipedeclassshuttle",
                StringComparison.OrdinalIgnoreCase))
        {
            return UseKnownTexture(
                repositoryRoot,
                assembly,
                texture,
                "assets/source/unified25/assets/ships-v2/small/" +
                "sheathipedeclassshuttle/Textures/standard.jpg",
                "TS_Save_43 model-compatible standard",
                diagnostics);
        }

        if (assembly.ShipId.Equals("xwing", StringComparison.OrdinalIgnoreCase)
            || assembly.ShipId.Equals("t65xwing", StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified25",
                "assets",
                "ships-v2",
                "small",
                "t65xwing",
                "Textures",
                "2",
                "luke.jpg");

            ValidateFile(path, "Luke Skywalker X-Wing V3 texture");

            var relative = NormalisePath(
                Path.GetRelativePath(repositoryRoot, path));

            diagnostics.Add(
                $"X-Wing ShipTexture corrected for V3 model compatibility: " +
                $"{texture.RepositoryPath} -> {relative}");

            return new PrototypeAssetInput
            {
                Role = texture.Role,
                AssetId = texture.AssetId,
                RepositoryPath = relative,
                FullPath = path,
                Exists = true
            };
        }

        if (assembly.ShipId.Equals(
                "tiereaper",
                StringComparison.OrdinalIgnoreCase)
            && !texture.RepositoryPath.Contains(
                "/ships-v2/",
                StringComparison.OrdinalIgnoreCase))
        {
            var folder = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified25",
                "assets",
                "ships-v2",
                "medium",
                "tierereaper",
                "Textures");

            var candidates = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.*")
                    .Where(path =>
                        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(path =>
                        Path.GetFileNameWithoutExtension(path).Equals(
                            "standard",
                            StringComparison.OrdinalIgnoreCase))
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            if (candidates.Count == 0)
            {
                throw new InvalidDataException(
                    $"No model-compatible TIE Reaper texture was found in '{folder}'.");
            }

            var relative = NormalisePath(
                Path.GetRelativePath(repositoryRoot, candidates[0]));

            diagnostics.Add(
                $"TIE Reaper ShipTexture corrected to model-local texture: " +
                $"{texture.RepositoryPath} -> {relative}");

            return new PrototypeAssetInput
            {
                Role = texture.Role,
                AssetId = texture.AssetId,
                RepositoryPath = relative,
                FullPath = candidates[0],
                Exists = true
            };
        }

        return texture;
    }

    private static PrototypeAssetInput CorrectXWingMultipartModel(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput model,
        ICollection<string> diagnostics)
    {
        if (!assembly.ShipId.Equals(
                "xwing",
                StringComparison.OrdinalIgnoreCase)
            && !assembly.ShipId.Equals(
                "t65xwing",
                StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        var shipFolder = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified25",
            "assets",
            "ships-v2",
            "small",
            "t65xwing");

        var baseModelPath = Path.Combine(
            shipFolder,
            "xwingbasev3.obj");
        var openModelPath = Path.Combine(
            shipFolder,
            "xwingopenv3.obj");
        var closedModelPath = Path.Combine(
            shipFolder,
            "xwingclosedv3.obj");

        ValidateFile(baseModelPath, "X-Wing base model");
        ValidateFile(openModelPath, "X-Wing open-wing state model");
        ValidateFile(closedModelPath, "X-Wing closed-wing state model");

        var relative = NormalisePath(
            Path.GetRelativePath(repositoryRoot, baseModelPath));

        if (!model.RepositoryPath.Equals(
                relative,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                $"X-Wing ShipModel corrected for visual prototype generation: " +
                $"{model.RepositoryPath} -> {relative}. Open/closed V3 state " +
                "models were also verified.");
        }

        return new PrototypeAssetInput
        {
            Role = model.Role,
            AssetId = model.AssetId,
            RepositoryPath = relative,
            FullPath = baseModelPath,
            Exists = true
        };
    }

    private static PrototypeAssetInput CorrectMisclassifiedBwingModel(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput model,
        ICollection<string> diagnostics)
    {
        if (!assembly.PegTemplateKey.Equals(
                "FirstEditionBwingShipPeg",
                StringComparison.OrdinalIgnoreCase)
            || !model.RepositoryPath.Contains(
                "/bases/pegs/",
                StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        var shipFolder = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified25",
            "assets",
            "ships-v2",
            "small",
            "asf01bwing");

        if (!Directory.Exists(shipFolder))
        {
            throw new DirectoryNotFoundException(
                $"The B-Wing model folder was not found: {shipFolder}");
        }

        var baseModelPath = Path.Combine(
            shipFolder,
            "bwing-base.obj");
        var closedModelPath = Path.Combine(
            shipFolder,
            "bwing-closed.obj");
        var openModelPath = Path.Combine(
            shipFolder,
            "bwing-open.obj");

        ValidateFile(
            baseModelPath,
            "B-Wing base model");
        ValidateFile(
            closedModelPath,
            "B-Wing closed-wing state model");
        ValidateFile(
            openModelPath,
            "B-Wing open-wing state model");

        var relative = NormalisePath(
            Path.GetRelativePath(
                repositoryRoot,
                baseModelPath));

        diagnostics.Add(
            $"B-Wing ShipModel corrected for structural prototype generation: " +
            $"{model.RepositoryPath} -> {relative}. " +
            "The open and closed wing-state models were also verified and will " +
            "be integrated when the B-Wing state-change runtime is implemented.");

        return new PrototypeAssetInput
        {
            Role = model.Role,
            AssetId = model.AssetId,
            RepositoryPath = relative,
            FullPath = baseModelPath,
            Exists = true
        };
    }

    private static IReadOnlyDictionary<string, JsonObject>
        LoadRequiredSnapshots(
            PrototypeRuntimeTemplateManifestInput manifest,
            IEnumerable<string> requiredKeys)
    {
        var result = new Dictionary<string, JsonObject>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in requiredKeys)
        {
            var template = manifest.Templates.FirstOrDefault(item =>
                item.TemplateKey.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase));

            if (template is null
                || !template.Status.Equals(
                    "Extracted",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(template.SnapshotPath))
            {
                throw new InvalidDataException(
                    $"Required extracted runtime template '{key}' is unavailable.");
            }

            ValidateFile(
                template.SnapshotPath,
                $"Runtime-template snapshot '{key}'");

            result[key] = JsonNode.Parse(
                    File.ReadAllText(template.SnapshotPath))?.AsObject()
                ?? throw new InvalidDataException(
                    $"Could not parse runtime-template snapshot '{key}'.");
        }

        return result;
    }

    private static PrototypeAssetInput RequireAsset(
        PrototypeAssemblyInput assembly,
        string role)
    {
        return assembly.Assets.FirstOrDefault(asset =>
                   asset.Role.Equals(
                       role,
                       StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidDataException(
                   $"{assembly.PackageId} has no selected {role} asset.");
    }

    private static void SetTransform(
        JsonObject obj,
        float x,
        float y,
        float z,
        float scale)
    {
        var transform = EnsureObject(
            obj,
            "Transform");

        transform["posX"] = x;
        transform["posY"] = y;
        transform["posZ"] = z;
        transform["rotX"] = 0.0;
        transform["rotY"] = 0.0;
        transform["rotZ"] = 0.0;
        transform["scaleX"] = scale;
        transform["scaleY"] = scale;
        transform["scaleZ"] = scale;
    }

    private static JsonObject EnsureObject(
        JsonObject parent,
        string property)
    {
        if (parent[property] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        parent[property] = created;
        return created;
    }

    private static JsonArray ToJsonArray(
        IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(value);
        return result;
    }

    private static JsonObject OpaqueWhite() =>
        new()
        {
            ["r"] = 1.0,
            ["g"] = 1.0,
            ["b"] = 1.0,
            ["a"] = 1.0
        };

    private static JsonObject VisiblePegWhite() =>
        new()
        {
            ["r"] = 1.0,
            ["g"] = 1.0,
            ["b"] = 1.0,
            ["a"] = 0.45
        };

    private static string NextGuid(
        string seed,
        ISet<string> used)
    {
        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var input = suffix == 0
                ? seed
                : $"{seed}:{suffix}";
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(input));
            var guid = Convert.ToHexString(bytes)[..6].ToLowerInvariant();

            if (used.Add(guid))
                return guid;
        }

        throw new InvalidOperationException(
            $"Could not allocate a unique TTS GUID for '{seed}'.");
    }

    private static string AssetUrl(
        string assetBaseUrl,
        string repositoryPath)
    {
        var normalised = repositoryPath
            .Replace('\\', '/')
            .TrimStart('/');

        const string unifiedPrefix =
            "assets/source/unified25/";

        if (normalised.StartsWith(
                unifiedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return
                "https://raw.githubusercontent.com/JohnnyCheese/" +
                "TTS_X-Wing2.0/master/" +
                normalised[unifiedPrefix.Length..];
        }

        return assetBaseUrl.TrimEnd('/') + "/" + normalised;
    }

    private static string ResolvePath(
        string repositoryRoot,
        string[] args,
        string option,
        string relativeDefault)
    {
        var explicitPath = ReadOption(args, option);

        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(
                repositoryRoot,
                relativeDefault.Replace(
                    '/',
                    Path.DirectorySeparatorChar))
            : Path.GetFullPath(explicitPath);
    }

    private static string ResolveOutputPath(
        string repositoryRoot,
        string[] args)
    {
        var explicitPath = ReadOption(args, "--output");

        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "prototypes",
                "XWing-1E-Phase12D-R6-Card-Dial-Backs-Prototype-Save.json")
            : Path.GetFullPath(explicitPath);
    }

    private static string ResolveAssetBaseUrl(
        string[] args)
    {
        var explicitUrl = ReadOption(
            args,
            "--asset-base-url");

        return string.IsNullOrWhiteSpace(explicitUrl)
            ? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/"
            : explicitUrl.TrimEnd('/') + "/";
    }

    private static string? ReadOption(
        string[] args,
        string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(
                    option,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static T Read<T>(
        string path)
    {
        return JsonSerializer.Deserialize<T>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidDataException(
                   $"Could not parse JSON file: {path}");
    }

    private static string NormalisePath(
        string path) =>
        path.Replace('\\', '/');

    private static void WriteReport(
        string path,
        PrototypeSaveGenerationManifest manifest,
        IReadOnlyList<PrototypeAssemblyInput> assemblies)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12D-1 – R6 First Edition Card and Dial Backs Prototype Save");
        writer.WriteLine();
        writer.WriteLine(
            $"Output: `{manifest.OutputSave}`");
        writer.WriteLine();
        writer.WriteLine(
            "| Ship | Pilot | Base | Peg |");
        writer.WriteLine("|---|---|---|---|");

        foreach (var assembly in assemblies)
        {
            writer.WriteLine(
                $"| {assembly.ShipName} | {assembly.PilotName} | " +
                $"{assembly.BaseTemplateKey} | {assembly.PegTemplateKey} |");
        }

        writer.WriteLine();
        writer.WriteLine(
            "This revision validates structural assembly and asset loading. " +
            "It retains the known-good bundled reference runtime. The generated " +
            "First Edition dial source will be bundled in the next runtime revision.");
        writer.WriteLine();
        writer.WriteLine(
            "The B-Wing structural model uses `bwing-base.obj`. " +
            "`bwing-open.obj` and `bwing-closed.obj` are validated as wing-state " +
            "models and are intentionally deferred to the B-Wing state runtime stage.");

        if (manifest.Diagnostics.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Diagnostics");
            foreach (var diagnostic in manifest.Diagnostics)
                writer.WriteLine($"- {diagnostic}");
        }
    }

    private static void ValidateFile(
        string path,
        string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"{description} was not found.",
                path);
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  generate-prototype-save <first-edition-repository> " +
            "<reference-save.json> [--assembly-plan <file>] " +
            "[--runtime-templates <file>] [--asset-base-url <url>] " +
            "[--output <file>]");
    }
}

public sealed class PrototypeSaveGenerationManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string ImplementationVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string ReferenceSave { get; init; } = string.Empty;
    public string AssemblyPlanPath { get; init; } = string.Empty;
    public string RuntimeTemplatesPath { get; init; } = string.Empty;
    public string OutputSave { get; init; } = string.Empty;
    public string AssetBaseUrl { get; init; } = string.Empty;
    public int AssembliesGenerated { get; init; }
    public int TtsObjectsGenerated { get; init; }
    public string RuntimeMode { get; init; } = string.Empty;
    public List<string> Diagnostics { get; init; } = new();
}

public sealed class PrototypeSaveAssemblyPlanInput
{
    public int InvalidPrototypeCount { get; init; }
    public List<string> ValidationErrors { get; init; } = new();
    public List<PrototypeAssemblyInput> Assemblies { get; init; } = new();
}

public sealed class PrototypeAssemblyInput
{
    public string RequestedShipId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string BaseSize { get; init; } = string.Empty;
    public string BaseTemplateKey { get; init; } = string.Empty;
    public string PegTemplateKey { get; init; } = string.Empty;
    public float PositionX { get; init; }
    public float PositionZ { get; init; }
    public List<string> MoveSet { get; init; } = new();
    public List<string> ActSet { get; init; } = new();
    public List<string> FirstEditionActions { get; init; } = new();
    public List<PrototypeAssetInput> Assets { get; init; } = new();
}

public sealed class PrototypeAssetInput
{
    public string Role { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool Exists { get; init; }
}

public sealed class PrototypeRuntimeTemplateManifestInput
{
    public List<PrototypeRuntimeTemplateInput> Templates { get; init; } = new();
}

public sealed class PrototypeRuntimeTemplateInput
{
    public string TemplateKey { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string AssetUrl { get; init; } = string.Empty;
    public string SnapshotPath { get; init; } = string.Empty;
}


public sealed class FirstEditionDialRuntimeInput
{
    public string Xml { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Modules { get; set; } =
        new Dictionary<string, string>();
    public IReadOnlyList<FirstEditionDialRuntimeAssetInput> Assets { get; set; } =
        Array.Empty<FirstEditionDialRuntimeAssetInput>();
}

public sealed class FirstEditionDialRuntimeAssetInput
{
    public string LogicalName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
