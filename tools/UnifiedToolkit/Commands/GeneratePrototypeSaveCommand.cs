using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
            var dialModelRepositoryPath = LoadFirstEditionDialModelPath(repositoryRoot);

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
            var assemblyDiagnostics = new List<PrototypeAssemblyAssetDiagnostic>();
            var generatedObjects = new JsonArray();
            var usedGuids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            for (var assemblyIndex = 0; assemblyIndex < assemblyPlan.Assemblies.Count; assemblyIndex++)
            {
                var assembly = assemblyPlan.Assemblies[assemblyIndex];
                var shipObjects = BuildAssemblyObjects(
                    repositoryRoot,
                    assetBaseUrl,
                    assembly,
                    baseTemplates,
                    pegIndex,
                    dialRuntime,
                    dialModelRepositoryPath,
                    assemblyIndex,
                    usedGuids,
                    diagnostics,
                    assemblyDiagnostics);

                foreach (var item in shipObjects)
                {
                    RepositoryAssetUrlPolicy.RewriteObjectUrls(item, assetBaseUrl);
                    RepositoryAssetUrlPolicy.ValidateNoUpstreamOrPrototypeShipTextures(item);
                    generatedObjects.Add(item);
                }
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

            var assetDiagnosticPath = Path.Combine(
                reportDirectory,
                "prototype-assembly-asset-diagnostics.json");

            var manifest = new PrototypeSaveGenerationManifest
            {
                SchemaVersion = "1.0.0",
                ImplementationVersion = "12E-5D-R1",
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
                AssetDiagnosticPath = NormalisePath(assetDiagnosticPath),
                RuntimeMode = "AssignedUnifiedDial-RepositoryOwnedAlignedModel-R2"
            };

            var manifestPath = Path.Combine(
                reportDirectory,
                "prototype-save-generation.json");
            var reportPath = Path.Combine(
                reportDirectory,
                "PROTOTYPE-SAVE-GENERATION.md");
            File.WriteAllText(
                assetDiagnosticPath,
                JsonSerializer.Serialize(assemblyDiagnostics, JsonOptions),
                new UTF8Encoding(false));

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteReport(reportPath, manifest, assemblyPlan.Assemblies);

            Console.WriteLine(
                "UnifiedToolkit Phase 12B-3 Structural Prototype Save Generation");
            Console.WriteLine(
                "=================================================================");
            Console.WriteLine("Implementation:          12E-5D R1 Independent Dial Front UV Transform");
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
            Console.WriteLine($"Asset diagnostics:       {assetDiagnosticPath}");
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
        string dialModelRepositoryPath,
        int assemblyIndex,
        ISet<string> usedGuids,
        ICollection<string> diagnostics,
        ICollection<PrototypeAssemblyAssetDiagnostic> assemblyDiagnostics)
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
            model,
            texture,
            diagnostics);

        ValidateShipAssetPolicy(assembly, model, "ShipModel");
        ValidateShipAssetPolicy(assembly, texture, "ShipTexture");

        var modelUrl = AssetUrl(assetBaseUrl, model.RepositoryPath);
        var textureUrl = AssetUrl(assetBaseUrl, texture.RepositoryPath);
        var textureReviewUrls = DiscoverShipTextureUrls(
            repositoryRoot,
            assetBaseUrl,
            texture);
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
            textureReviewUrls,
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
            assetBaseUrl,
            dialModelRepositoryPath,
            assemblyIndex);

        var cardObject = BuildPilotCard(
            repositoryRoot,
            assetBaseUrl,
            cardGuid,
            assembly,
            cardUrl);

        assemblyDiagnostics.Add(new PrototypeAssemblyAssetDiagnostic
        {
            PackageId = assembly.PackageId,
            ShipId = assembly.ShipId,
            ShipName = assembly.ShipName,
            PilotId = assembly.PilotId,
            PilotName = assembly.PilotName,
            Faction = assembly.Faction,
            BaseSize = assembly.BaseSize,
            BaseGuid = baseGuid,
            DialGuid = dialGuid,
            CardGuid = cardGuid,
            BaseTemplateKey = assembly.BaseTemplateKey,
            BaseTextureUrl = customBaseTextureUrl(baseTemplate),
            PegTemplateKey = assembly.PegTemplateKey,
            PegAsset = pegUrl,
            PilotTokenAsset = pilotToken.RepositoryPath,
            ShipModelAsset = model.RepositoryPath,
            ShipTextureAsset = texture.RepositoryPath,
            DialModelAsset = dialModelRepositoryPath,
            DialTextureAsset = dialTexture.RepositoryPath,
            PilotCardAsset = pilotCard.RepositoryPath,
            ChildHierarchy = DescribeChildHierarchy(baseTemplate)
        });

        return new List<JsonObject>
        {
            baseTemplate,
            dialTemplate,
            cardObject
        };
    }

    private static string customBaseTextureUrl(JsonObject baseObject)
    {
        return baseObject["CustomMesh"]?["DiffuseURL"]?.GetValue<string>()
            ?? string.Empty;
    }

    private static List<PrototypeObjectHierarchyDiagnostic> DescribeChildHierarchy(
        JsonObject baseObject)
    {
        var results = new List<PrototypeObjectHierarchyDiagnostic>();
        if (baseObject["ChildObjects"] is not JsonArray children)
            return results;

        foreach (var node in children)
        {
            if (node is not JsonObject child)
                continue;

            var mesh = child["CustomMesh"] as JsonObject;
            var image = child["CustomImage"] as JsonObject;
            var transform = child["Transform"] as JsonObject;

            results.Add(new PrototypeObjectHierarchyDiagnostic
            {
                Guid = child["GUID"]?.GetValue<string>() ?? string.Empty,
                Name = child["Name"]?.GetValue<string>() ?? string.Empty,
                Nickname = child["Nickname"]?.GetValue<string>() ?? string.Empty,
                MeshUrl = mesh?["MeshURL"]?.GetValue<string>() ?? string.Empty,
                DiffuseUrl = mesh?["DiffuseURL"]?.GetValue<string>()
                    ?? image?["ImageURL"]?.GetValue<string>()
                    ?? string.Empty,
                PositionY = transform?["posY"]?.GetValue<double>() ?? 0.0,
                ScaleX = transform?["scaleX"]?.GetValue<double>() ?? 0.0,
                ScaleY = transform?["scaleY"]?.GetValue<double>() ?? 0.0,
                ScaleZ = transform?["scaleZ"]?.GetValue<double>() ?? 0.0
            });
        }

        return results;
    }

    private static void ConfigureBase(
        JsonObject baseObject,
        string baseGuid,
        PrototypeAssemblyInput assembly,
        string tokenUrl,
        string modelUrl,
        string textureUrl,
        IReadOnlyList<string> textureReviewUrls,
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
                textureReviewUrls,
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

        if (textureReviewUrls.Count > 1)
        {
            baseObject["LuaScript"] = BuildTextureReviewLua();
            baseObject["LuaScriptState"] = new JsonObject
            {
                ["currentTexture"] = textureUrl,
                ["shipGuid"] = shipGuid,
                ["shipLocalPosition"] = new JsonObject
                {
                    ["x"] = -1.18049023e-07,
                    ["y"] = 3.49761486,
                    ["z"] = 6.2130125e-15
                },
                ["shipLocalRotation"] = new JsonObject
                {
                    ["x"] = 0.0,
                    ["y"] = 0.0,
                    ["z"] = 0.0
                },
                ["textures"] = new JsonArray(
                    textureReviewUrls
                        .Select(url => JsonValue.Create(url))
                        .ToArray())
            }.ToJsonString();
        }
        else
        {
            baseObject["LuaScript"] = string.Empty;
            baseObject["LuaScriptState"] = string.Empty;
        }

        baseObject["XmlUI"] = string.Empty;
        baseObject["CustomUIAssets"] = new JsonArray();
    }

    private const double SmallPilotTokenScale = 1.10;
    private const double LargePilotTokenScale = 2.13;
    private const double EpicPilotTokenScale = 1.00;

    private static JsonObject BuildPilotTokenChild(
        string guid,
        string tokenUrl,
        PrototypeAssemblyInput assembly)
    {
        var scale = assembly.BaseSize.ToLowerInvariant() switch
        {
            "small" => SmallPilotTokenScale,
            "large" => LargePilotTokenScale,
            "epic" => EpicPilotTokenScale,
            _ => throw new InvalidDataException(
                $"Unsupported First Edition base size for pilot-token scaling: " +
                $"{assembly.BaseSize}")
        };

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

        if (assembly.ShipId.Equals("t70xwing", StringComparison.OrdinalIgnoreCase))
        {
            openPath =
                "assets/source/unified25/assets/ships-v2/small/t70xwing/t70_openv2.obj";
            closedPath =
                "assets/source/unified25/assets/ships-v2/small/t70xwing/t70_closedv2.obj";
        }
        else if (assembly.ShipId.Equals("xwing", StringComparison.OrdinalIgnoreCase)
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
        IReadOnlyList<string> textureReviewUrls,
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

    private static string BuildTextureReviewLua() =>
        """
        local textureReview = {}
        local textureChangeInProgress = false

        function onLoad(savedData)
            if savedData ~= nil and savedData ~= "" then
                local ok, decoded = pcall(JSON.decode, savedData)
                if ok and decoded ~= nil then
                    textureReview = decoded
                end
            end

            if textureReview.textures == nil then
                textureReview.textures = {}
            end

            if textureReview.shipLocalPosition == nil then
                textureReview.shipLocalPosition = {
                    x = -0.000000118049023,
                    y = 3.49761486,
                    z = 0.0000000000000062130125
                }
            end

            if textureReview.shipLocalRotation == nil then
                textureReview.shipLocalRotation = {
                    x = 0,
                    y = 0,
                    z = 0
                }
            end

            self.clearContextMenu()

            if #textureReview.textures > 1 then
                self.addContextMenuItem("Next Texture", NextTexture, false)
            end
        end

        local function findShipAttachment()
            local shipGuid = textureReview.shipGuid or ""

            for _, attachment in ipairs(self.getAttachments()) do
                if attachment.guid == shipGuid then
                    return attachment
                end
            end

            return nil
        end

        local function exactShipWorldPosition()
            return self.positionToWorld(textureReview.shipLocalPosition)
        end

        local function exactShipWorldRotation()
            local baseRotation = self.getRotation()
            local localRotation = textureReview.shipLocalRotation

            return {
                x = baseRotation.x + localRotation.x,
                y = baseRotation.y + localRotation.y,
                z = baseRotation.z + localRotation.z
            }
        end

        local function restoreShipTransform(ship)
            ship.setLock(true)
            ship.setVelocity({ 0, 0, 0 })
            ship.setAngularVelocity({ 0, 0, 0 })
            ship.setPosition(exactShipWorldPosition())
            ship.setRotation(exactShipWorldRotation())
        end

        local function reattachShip(ship)
            if ship == nil then
                textureChangeInProgress = false
                print("Could not restore the ship model attachment for " .. self.getName())
                return
            end

            restoreShipTransform(ship)

            Wait.frames(
                function()
                    restoreShipTransform(ship)
                    self.addAttachment(ship)
                    textureChangeInProgress = false
                end,
                1)
        end

        local function applyTextureAfterDetach(nextTexture)
            local shipGuid = textureReview.shipGuid or ""
            local ship = getObjectFromGUID(shipGuid)

            if ship == nil then
                textureChangeInProgress = false
                print("Could not find the detached ship model for " .. self.getName())
                return
            end

            restoreShipTransform(ship)
            ship.setCustomObject({ diffuse = nextTexture })

            Wait.frames(
                function()
                    reattachShip(ship)
                end,
                2)
        end

        function NextTexture()
            if textureChangeInProgress then
                print("Texture change already in progress for " .. self.getName())
                return
            end

            local textures = textureReview.textures or {}
            if #textures <= 1 then
                print("No alternative ship textures are registered for " .. self.getName())
                return
            end

            local attachment = findShipAttachment()
            if attachment == nil then
                print("Could not find the attached ship model for " .. self.getName())
                return
            end

            local current = textureReview.currentTexture or ""
            local currentIndex = 0

            for index, url in ipairs(textures) do
                if url == current then
                    currentIndex = index
                    break
                end
            end

            local nextIndex = (currentIndex % #textures) + 1
            local nextTexture = textures[nextIndex]

            textureReview.currentTexture = nextTexture
            self.script_state = JSON.encode(textureReview)
            textureChangeInProgress = true

            self.removeAttachment(attachment.index)

            Wait.frames(
                function()
                    applyTextureAfterDetach(nextTexture)
                end,
                1)

            print(
                self.getName()
                .. " texture "
                .. tostring(nextIndex)
                .. "/"
                .. tostring(#textures)
                .. ": "
                .. nextTexture)
        end
        """;

    private static IReadOnlyList<string> DiscoverShipTextureUrls(
        string repositoryRoot,
        string assetBaseUrl,
        PrototypeAssetInput selectedTexture)
    {
        var selectedPath = Path.GetFullPath(selectedTexture.FullPath);
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(selectedPath)
            ?? throw new InvalidDataException(
                $"Ship texture has no parent folder: {selectedPath}"));

        DirectoryInfo? texturesRoot = directory;
        while (texturesRoot is not null
               && !texturesRoot.Name.Equals(
                   "Textures",
                   StringComparison.OrdinalIgnoreCase))
        {
            texturesRoot = texturesRoot.Parent;
        }

        texturesRoot ??= directory;

        var supportedExtensions = new HashSet<string>(
            new[] { ".png", ".jpg", ".jpeg", ".webp" },
            StringComparer.OrdinalIgnoreCase);

        var discovered = texturesRoot
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => supportedExtensions.Contains(file.Extension))
            .Select(file => new
            {
                FullPath = file.FullName,
                RelativePath = NormalisePath(
                    Path.GetRelativePath(repositoryRoot, file.FullName))
            })
            .Where(item => item.RelativePath.Contains(
                "/assets/ships-v2/",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => AssetUrl(assetBaseUrl, item.RelativePath))
            .ToList();

        var selectedUrl = AssetUrl(
            assetBaseUrl,
            selectedTexture.RepositoryPath);

        discovered.RemoveAll(url =>
            url.Equals(selectedUrl, StringComparison.OrdinalIgnoreCase));
        discovered.Insert(0, selectedUrl);

        return discovered
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string LoadFirstEditionDialModelPath(string repositoryRoot)
    {
        var manifestPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase12e",
            "dial-model-generation",
            "first-edition-dial-model.json");

        ValidateFile(manifestPath, "Phase 12E dial-model manifest");

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the Phase 12E dial-model manifest.");

        var outputPath = manifest["outputPath"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidDataException(
                "The Phase 12E dial-model manifest has no outputPath.");
        }

        var normalized = outputPath.Replace('\\', '/').TrimStart('/');
        var localPath = Path.Combine(
            repositoryRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar));
        ValidateFile(localPath, "Generated First Edition dial model");

        return normalized;
    }

    private static void ConfigureDial(
        JsonObject dialObject,
        string dialGuid,
        string baseGuid,
        PrototypeAssemblyInput assembly,
        string dialTextureUrl,
        FirstEditionDialRuntimeInput dialRuntime,
        string assetBaseUrl,
        string dialModelRepositoryPath,
        int assemblyIndex)
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
            assembly.PositionZ - 5.8f,
            0.70f);

        var transform = dialObject["Transform"]?.AsObject()
            ?? throw new InvalidDataException(
                "Assigned dial template has no Transform object.");
        transform["rotX"] = 0.0;
        transform["rotY"] = 180.0;
        transform["rotZ"] = 0.0;
        transform["scaleX"] = 0.70;
        transform["scaleY"] = 0.70;
        transform["scaleZ"] = 0.70;

        var customMesh = dialObject["CustomMesh"]?.AsObject()
            ?? throw new InvalidDataException(
                "Assigned dial template has no CustomMesh object.");
        customMesh["MeshURL"] = AssetUrl(
            assetBaseUrl,
            dialModelRepositoryPath);
        customMesh["DiffuseURL"] = dialTextureUrl;

        var bundledLua = dialObject["LuaScript"]?.GetValue<string>()
            ?? throw new InvalidDataException(
                "Assigned dial template has no bundled Lua runtime.");

        var integratedLua = ReplaceBundledDialModules(
            bundledLua,
            dialRuntime.Modules);
        dialObject["LuaScript"] = StaggerAssignedShipRestore(
            integratedLua,
            assemblyIndex);

        var mergedUiAssets = MergeDialUiAssets(
            dialObject["CustomUIAssets"] as JsonArray,
            dialRuntime.Assets);
        var namespacedUi = NamespaceDialUiAssets(
            dialRuntime.Xml,
            mergedUiAssets,
            dialGuid);

        dialObject["XmlUI"] = namespacedUi.Xml;
        dialObject["CustomUIAssets"] = namespacedUi.Assets;

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

    private static string StaggerAssignedShipRestore(
        string lua,
        int assemblyIndex)
    {
        const string restorePattern =
            @"Wait\.condition\(\s*" +
            @"function\(\)\s*" +
            @"local\s+savedShip\s*=\s*getObjectFromGUID\(savedShipGuid\)\s*" +
            @"if\s+savedShip\s+then\s*" +
            @"dial\.call\(""assignShip"",\s*\{\s*ship\s*=\s*savedShip\s*\}\)\s*" +
            @"end\s*" +
            @"end,\s*" +
            @"function\(\)\s*" +
            @"local\s+savedShip\s*=\s*getObjectFromGUID\(savedShipGuid\)\s*" +
            @"return\s+savedShip\s*~=\s*nil" +
            @"(?:\s+and\s+not\s+savedShip\.loading_custom)?\s*" +
            @"end,\s*30\s*\)";

        var matches = Regex.Matches(
            lua,
            restorePattern,
            RegexOptions.CultureInvariant);

        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one assigned-ship restore block in the dial Lua, but found {matches.Count}.");
        }

        var initialDelayFrames = 30 + (assemblyIndex * 45);
        var replacementBlock =
            $$"""
            local restoreAttempts = 0
            local function restoreAssignedShip()
                local savedShip = getObjectFromGUID(savedShipGuid)
                if savedShip then
                    dial.call("assignShip", { ship = savedShip })
                    return
                end

                restoreAttempts = restoreAttempts + 1
                if restoreAttempts < 120 then
                    Wait.frames(restoreAssignedShip, 15)
                else
                    print(
                        "First Edition dial could not restore assigned ship "
                        .. tostring(savedShipGuid))
                end
            end
            Wait.frames(restoreAssignedShip, {{initialDelayFrames}})
            """;

        return Regex.Replace(
            lua,
            restorePattern,
            _ => replacementBlock,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
    }

    private static NamespacedDialUi NamespaceDialUiAssets(
        string xml,
        JsonArray assets,
        string dialGuid)
    {
        if (string.IsNullOrWhiteSpace(dialGuid))
            throw new ArgumentException("Dial GUID is required.", nameof(dialGuid));

        var prefix = $"dial_{dialGuid}_";
        var namespacedAssets = assets.DeepClone().AsArray();
        var namespacedXml = xml;
        var renamedCount = 0;

        foreach (var node in namespacedAssets)
        {
            if (node is not JsonObject asset)
                continue;

            var originalName = asset["Name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(originalName))
                continue;

            var namespacedName = prefix + originalName;
            asset["Name"] = namespacedName;

            namespacedXml = namespacedXml
                .Replace(
                    $"\"{originalName}\"",
                    $"\"{namespacedName}\"",
                    StringComparison.Ordinal)
                .Replace(
                    $"'{originalName}'",
                    $"'{namespacedName}'",
                    StringComparison.Ordinal);

            renamedCount++;
        }

        if (renamedCount == 0)
        {
            throw new InvalidDataException(
                $"Dial '{dialGuid}' has no custom UI assets to namespace.");
        }

        return new NamespacedDialUi
        {
            Xml = namespacedXml,
            Assets = namespacedAssets,
            RenamedAssetCount = renamedCount
        };
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
                ["posZ"] = assembly.PositionZ - 2.6,
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

        var colour = FactionColourPalette.GetPrimary(factionId);

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

        var colour = FactionColourPalette.GetPrimary(factionId);

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

    private static void ValidateShipAssetPolicy(
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput asset,
        string role)
    {
        const string requiredRoot =
            "assets/source/unified25/assets/ships-v2/";
        var path = asset.RepositoryPath.Replace('\\', '/');

        if (!path.StartsWith(requiredRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assembly.PilotName} {role} must resolve under " +
                $"'{requiredRoot}', but resolved to '{asset.RepositoryPath}'.");
        }

        if (path.Contains(
                "/PrototypeShipTexture/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assembly.PilotName} {role} resolved to a prohibited " +
                "generated PrototypeShipTexture asset.");
        }
    }

    private static PrototypeAssetInput CorrectKnownPrototypeShipModel(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput model,
        ICollection<string> diagnostics)
    {
        string? relative = null;

        if (assembly.ShipId.Equals(
                "alphaclassstarwing",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/small/" +
                "alphaclassstarwing/Alpha Class2.obj";
        }
        else if (assembly.ShipId.Equals(
                "firespray31",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/medium/" +
                "firesprayclasspatrolcraft/firesprayV2.obj";
        }
        else if (assembly.ShipId.Equals(
                "tieadvanced",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/small/" +
                "tieadvancedx1/tieadvancedx1v2.obj";
        }
        else if (assembly.ShipId.Equals(
                "tieadvprototype",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/small/" +
                "tieadvancedv1/tieadvv1v2.obj";
        }
        else if (assembly.ShipId.Equals(
                "tiefofighter",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/small/" +
                "tiefofighter/TieFOv2.obj";
        }
        else if (assembly.ShipId.Equals(
                "t70xwing",
                StringComparison.OrdinalIgnoreCase))
        {
            relative =
                "assets/source/unified25/assets/ships-v2/small/" +
                "t70xwing/t70_basev2.obj";
        }
        else if (assembly.ShipId.Equals(
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
                $"{assembly.ShipName} ShipModel corrected from confirmed Unified 2.5 object: " +
                $"{model.RepositoryPath} -> {relative}");

            RecordShipModelSelectionAudit(
                repositoryRoot,
                assembly,
                model.RepositoryPath,
                relative,
                "Confirmed by comparing the generated validation save with a working Unified 2.5 spawned-ship save.",
                "CleanupCandidate");
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

    private static void RecordShipModelSelectionAudit(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        string rejectedModelPath,
        string selectedModelPath,
        string evidence,
        string cleanupStatus)
    {
        if (rejectedModelPath.Equals(
                selectedModelPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reportFolder = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "model-selection");
        Directory.CreateDirectory(reportFolder);

        var jsonPath = Path.Combine(
            reportFolder,
            "ship-model-selection-audit.json");
        var csvPath = Path.Combine(
            reportFolder,
            "ship-model-selection-audit.csv");
        var markdownPath = Path.Combine(
            reportFolder,
            "SHIP-MODEL-SELECTION-AUDIT.md");

        var entries = File.Exists(jsonPath)
            ? JsonSerializer.Deserialize<List<ShipModelSelectionAuditEntry>>(
                  File.ReadAllText(jsonPath),
                  JsonOptions)
              ?? new List<ShipModelSelectionAuditEntry>()
            : new List<ShipModelSelectionAuditEntry>();

        var existingIndex = entries.FindIndex(entry =>
            entry.Faction.Equals(
                assembly.Faction,
                StringComparison.OrdinalIgnoreCase)
            && entry.ShipId.Equals(
                assembly.ShipId,
                StringComparison.OrdinalIgnoreCase)
            && entry.RejectedModelPath.Equals(
                rejectedModelPath,
                StringComparison.OrdinalIgnoreCase)
            && entry.SelectedModelPath.Equals(
                selectedModelPath,
                StringComparison.OrdinalIgnoreCase));

        var entry = new ShipModelSelectionAuditEntry
        {
            Faction = assembly.Faction,
            ShipId = assembly.ShipId,
            ShipName = assembly.ShipName,
            RejectedModelPath = NormalisePath(rejectedModelPath),
            SelectedModelPath = NormalisePath(selectedModelPath),
            Evidence = evidence,
            CleanupStatus = cleanupStatus,
            LastConfirmedUtc = DateTimeOffset.UtcNow
        };

        if (existingIndex >= 0)
            entries[existingIndex] = entry;
        else
            entries.Add(entry);

        entries = entries
            .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ShipName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RejectedModelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(entries, JsonOptions),
            new UTF8Encoding(false));

        using (var writer = new StreamWriter(
                   csvPath,
                   append: false,
                   new UTF8Encoding(false)))
        {
            writer.WriteLine(
                "Faction,ShipId,ShipName,RejectedModelPath,SelectedModelPath," +
                "Evidence,CleanupStatus,LastConfirmedUtc");

            foreach (var item in entries)
            {
                writer.WriteLine(string.Join(
                    ",",
                    CsvValue(item.Faction),
                    CsvValue(item.ShipId),
                    CsvValue(item.ShipName),
                    CsvValue(item.RejectedModelPath),
                    CsvValue(item.SelectedModelPath),
                    CsvValue(item.Evidence),
                    CsvValue(item.CleanupStatus),
                    CsvValue(item.LastConfirmedUtc.ToString("O"))));
            }
        }

        using (var writer = new StreamWriter(
                   markdownPath,
                   append: false,
                   new UTF8Encoding(false)))
        {
            writer.WriteLine("# Ship Model Selection Audit");
            writer.WriteLine();
            writer.WriteLine(
                "This report records model links rejected during visual validation " +
                "and the confirmed OBJ selected instead.");
            writer.WriteLine();
            writer.WriteLine(
                "> Files are never deleted automatically. Review all references, " +
                "states, variants and duplicate-content records before cleanup.");
            writer.WriteLine();
            writer.WriteLine("| Faction | Ship | Rejected OBJ | Confirmed OBJ | Status |");
            writer.WriteLine("|---|---|---|---|---|");

            foreach (var item in entries)
            {
                writer.WriteLine(
                    $"| {MarkdownCell(item.Faction)} " +
                    $"| {MarkdownCell(item.ShipName)} " +
                    $"| `{MarkdownCell(item.RejectedModelPath)}` " +
                    $"| `{MarkdownCell(item.SelectedModelPath)}` " +
                    $"| {MarkdownCell(item.CleanupStatus)} |");
            }

            writer.WriteLine();
            writer.WriteLine("## Evidence");
            writer.WriteLine();

            foreach (var item in entries)
            {
                writer.WriteLine(
                    $"- **{item.Faction} / {item.ShipName}:** {item.Evidence}");
            }
        }
    }

    private static string CsvValue(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string MarkdownCell(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string? ResolveChildDirectoryCaseInsensitive(
        string parentFolder,
        string expectedName)
    {
        if (!Directory.Exists(parentFolder))
            return null;

        var matches = Directory
            .EnumerateDirectories(parentFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Equals(
                expectedName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 0)
            return null;

        if (matches.Count > 1)
        {
            throw new InvalidDataException(
                $"Multiple case-variant texture directories were found beneath " +
                $"{parentFolder}: {string.Join(", ", matches.Select(Path.GetFileName))}");
        }

        return matches[0];
    }

    private static string ResolvePilotSpecificShipTexture(
        PrototypeAssemblyInput assembly,
        string texturesFolder,
        IReadOnlyList<string> rootTextures,
        string fallbackPath)
    {
        string? fileName = null;

        if (assembly.Faction.Equals(
                "galacticempire",
                StringComparison.OrdinalIgnoreCase)
            && assembly.ShipId.Equals(
                "firespray31",
                StringComparison.OrdinalIgnoreCase))
        {
            fileName = assembly.PilotName switch
            {
                "Bounty Hunter" => "standard.jpg",
                "Krassis Trelix" => "krassis.jpg",
                "Kath Scarlet" => "kath.jpg",
                "Boba Fett" => "boba.jpg",
                _ => null
            };
        }
        else if (assembly.Faction.Equals(
                     "galacticempire",
                     StringComparison.OrdinalIgnoreCase)
                 && assembly.ShipId.Equals(
                     "tieadvanced",
                     StringComparison.OrdinalIgnoreCase))
        {
            fileName = assembly.PilotName switch
            {
                "Tempest Squadron Pilot" => "standard.jpg",
                "Lieutenant Colzet" => "standard.jpg",
                "Storm Squadron Pilot" => "standard.jpg",
                "Commander Alozen" => "blue.jpg",
                "Zertik Strom" => "blue.jpg",
                "Maarek Stele" => "standard.jpg",
                "Juno Eclipse" => "blue.jpg",
                "Darth Vader" => "blue.jpg",
                _ => null
            };
        }

        if (fileName is null)
            return fallbackPath;

        var selected = rootTextures.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(
                fileName,
                StringComparison.OrdinalIgnoreCase));

        if (selected is null)
        {
            throw new InvalidDataException(
                $"{assembly.PilotName} — {assembly.ShipName} default texture " +
                $"was not found. Expected: " +
                $"{NormalisePath(Path.Combine(texturesFolder, fileName))}");
        }

        return selected;
    }

    private static PrototypeAssetInput CorrectPrototypeShipTexture(
        string repositoryRoot,
        PrototypeAssemblyInput assembly,
        PrototypeAssetInput model,
        PrototypeAssetInput linkedTexture,
        ICollection<string> diagnostics)
    {
        var modelPath = Path.GetFullPath(model.FullPath);
        var shipFolder = Path.GetDirectoryName(modelPath)
            ?? throw new InvalidDataException(
                $"{assembly.ShipName} model has no parent folder: {modelPath}");

        var texturesFolder = ResolveChildDirectoryCaseInsensitive(
            shipFolder,
            "Textures");

        if (texturesFolder is null)
        {
            throw new InvalidDataException(
                $"{assembly.ShipName} model folder has no sibling texture directory " +
                $"named Textures or textures beneath: " +
                NormalisePath(Path.GetRelativePath(repositoryRoot, shipFolder)));
        }

        var supportedExtensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".webp" },
            StringComparer.OrdinalIgnoreCase);

        var rootTextures = Directory
            .EnumerateFiles(texturesFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rootTextures.Count == 0)
        {
            throw new InvalidDataException(
                $"{assembly.ShipName} has no root-level texture images in: " +
                NormalisePath(Path.GetRelativePath(repositoryRoot, texturesFolder)));
        }

        var preferredStandardJpeg = rootTextures.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(
                "standard.jpg",
                StringComparison.OrdinalIgnoreCase));

        var alternateStandard = rootTextures.FirstOrDefault(path =>
            Path.GetFileNameWithoutExtension(path).Equals(
                "standard",
                StringComparison.OrdinalIgnoreCase));

        var selectedPath =
            preferredStandardJpeg
            ?? alternateStandard
            ?? rootTextures[0];

        selectedPath = ResolvePilotSpecificShipTexture(
            assembly,
            texturesFolder,
            rootTextures,
            selectedPath);

        var selectedRelative = NormalisePath(
            Path.GetRelativePath(repositoryRoot, selectedPath));

        if (preferredStandardJpeg is null)
        {
            var reason = alternateStandard is not null
                ? $"standard.jpg is missing; temporarily using {Path.GetFileName(alternateStandard)}"
                : $"standard.jpg is missing; temporarily using the first root-level texture {Path.GetFileName(selectedPath)}";

            diagnostics.Add(
                $"{assembly.ShipName} ShipTexture fallback: {reason}. " +
                $"Create {NormalisePath(Path.Combine(
                    Path.GetRelativePath(repositoryRoot, texturesFolder),
                    "standard.jpg"))} to remove this fallback.");
        }

        if (!linkedTexture.RepositoryPath.Equals(
                selectedRelative,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                $"{assembly.ShipName} ShipTexture resolved from corrected model folder: " +
                $"{linkedTexture.RepositoryPath} -> {selectedRelative}");
        }

        return new PrototypeAssetInput
        {
            Role = linkedTexture.Role,
            AssetId = linkedTexture.AssetId,
            RepositoryPath = selectedRelative,
            FullPath = selectedPath,
            Exists = true
        };
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

            RecordShipModelSelectionAudit(
                repositoryRoot,
                assembly,
                model.RepositoryPath,
                relative,
                "Confirmed multipart X-Wing base model; open and closed V3 state models were also verified.",
                "CleanupCandidate");
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

        RecordShipModelSelectionAudit(
            repositoryRoot,
            assembly,
            model.RepositoryPath,
            relative,
            "The linked ShipModel was not the confirmed B-Wing base model; the base/open/closed model set was verified.",
            "ReviewLinkAndCleanup");

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

public sealed class ShipModelSelectionAuditEntry
{
    public string Faction { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string RejectedModelPath { get; init; } = string.Empty;
    public string SelectedModelPath { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string CleanupStatus { get; init; } = string.Empty;
    public DateTimeOffset LastConfirmedUtc { get; init; }
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
    public string AssetDiagnosticPath { get; init; } = string.Empty;
    public List<string> Diagnostics { get; init; } = new();
}


public sealed class PrototypeAssemblyAssetDiagnostic
{
    public string PackageId { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string BaseSize { get; init; } = string.Empty;
    public string BaseGuid { get; init; } = string.Empty;
    public string DialGuid { get; init; } = string.Empty;
    public string CardGuid { get; init; } = string.Empty;
    public string BaseTemplateKey { get; init; } = string.Empty;
    public string BaseTextureUrl { get; init; } = string.Empty;
    public string PegTemplateKey { get; init; } = string.Empty;
    public string PegAsset { get; init; } = string.Empty;
    public string PilotTokenAsset { get; init; } = string.Empty;
    public string ShipModelAsset { get; init; } = string.Empty;
    public string ShipTextureAsset { get; init; } = string.Empty;
    public string DialModelAsset { get; init; } = string.Empty;
    public string DialTextureAsset { get; init; } = string.Empty;
    public string PilotCardAsset { get; init; } = string.Empty;
    public List<PrototypeObjectHierarchyDiagnostic> ChildHierarchy { get; init; } = new();
}

public sealed class PrototypeObjectHierarchyDiagnostic
{
    public string Guid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string MeshUrl { get; init; } = string.Empty;
    public string DiffuseUrl { get; init; } = string.Empty;
    public double PositionY { get; init; }
    public double ScaleX { get; init; }
    public double ScaleY { get; init; }
    public double ScaleZ { get; init; }
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


public sealed class NamespacedDialUi
{
    public string Xml { get; init; } = string.Empty;
    public JsonArray Assets { get; init; } = new();
    public int RenamedAssetCount { get; init; }
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
