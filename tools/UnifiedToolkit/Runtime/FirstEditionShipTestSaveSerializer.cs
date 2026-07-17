using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Runtime;

public sealed class FirstEditionShipTestSaveResult
{
    public string SchemaVersion { get; set; } = "1.2";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string ObjectModelPath { get; set; } = "";
    public string UnifiedSavePath { get; set; } = "";
    public string UnifiedRepositoryPath { get; set; } = "";
    public string OutputSavePath { get; set; } = "";
    public string ShipId { get; set; } = "";
    public string ShipName { get; set; } = "";
    public string PilotName { get; set; } = "";
    public string AppearanceName { get; set; } = "";
    public string BaseSize { get; set; } = "";
    public int ObjectCount { get; set; }
    public bool BaseSerialized { get; set; }
    public bool PegSerialized { get; set; }
    public bool PrimaryShipModelSerialized { get; set; }
    public bool VisibleConfigurationSerialized { get; set; }
    public bool AlternateConfigurationRecorded { get; set; }
    public bool GlobalLuaRemoved { get; set; }
    public bool ObjectScriptsRemoved { get; set; }
    public bool ReadyForTtsLoadTest { get; set; }
    public FirstEditionConfigurationMetadata Configuration { get; set; } = new();
    public List<FirstEditionSerializedObjectRecord> Objects { get; set; } = new();
    public List<FirstEditionAssetResolutionRecord> AssetResolutions { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> ReviewNotes { get; set; } = new();
}

public sealed class FirstEditionConfigurationMetadata
{
    public string PrimaryMesh { get; set; } = "";
    public string VisibleConfigurationName { get; set; } = "";
    public string VisibleConfigurationMesh { get; set; } = "";
    public string AlternateConfigurationName { get; set; } = "";
    public string AlternateConfigurationMesh { get; set; } = "";
    public string SharedTexture { get; set; } = "";
}

public sealed class FirstEditionSerializedObjectRecord
{
    public string Component { get; set; } = "";
    public string Guid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string MeshUrl { get; set; } = "";
    public string DiffuseUrl { get; set; } = "";
    public string ColliderUrl { get; set; } = "";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationY { get; set; }
    public float Scale { get; set; }
}

public sealed class FirstEditionAssetResolutionRecord
{
    public string Component { get; set; } = "";
    public string Role { get; set; } = "";
    public string OriginalValue { get; set; } = "";
    public string ResolvedValue { get; set; } = "";
    public string Source { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
}

public static class FirstEditionShipTestSaveSerializer
{
    private const string GitHubRawPrefix = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/";
    private const string VerifyCache = "{verifycache}";
    private const float RuntimeScale = 0.629f;
    private const float ShipY = 3.497615f;

    public static FirstEditionShipTestSaveResult Serialize(
        string objectModelPath,
        string unifiedSavePath,
        string unifiedRepositoryPath,
        string outputSavePath)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var document = JsonSerializer.Deserialize<FirstEditionShipObjectModelDocument>(File.ReadAllText(objectModelPath), options)
            ?? throw new InvalidDataException("Could not deserialize the First Edition ship object-model document.");
        if (!document.Summary.ReadyForSerializationReview || document.ObjectModel is null)
            throw new InvalidDataException("The object-model report is not ready for serialization review.");

        var model = document.ObjectModel;
        ValidateModel(model);
        ValidateSupportedRuntimeRecipe(model);

        unifiedRepositoryPath = Path.GetFullPath(unifiedRepositoryPath);
        if (!Directory.Exists(unifiedRepositoryPath))
            throw new DirectoryNotFoundException($"Unified repository folder was not found: {unifiedRepositoryPath}");

        var root = JsonNode.Parse(File.ReadAllText(unifiedSavePath))?.AsObject()
            ?? throw new InvalidDataException("Could not parse the Unified save envelope.");
        var prototype = FindObjectByGuid(root, model.Base.PrototypeGuid)
            ?? throw new InvalidDataException($"Composite base prototype GUID '{model.Base.PrototypeGuid}' was not found in the Unified save.");

        var result = new FirstEditionShipTestSaveResult
        {
            ObjectModelPath = Path.GetFullPath(objectModelPath),
            UnifiedSavePath = Path.GetFullPath(unifiedSavePath),
            UnifiedRepositoryPath = unifiedRepositoryPath,
            OutputSavePath = Path.GetFullPath(outputSavePath),
            ShipId = model.ShipId,
            ShipName = model.ShipName,
            PilotName = model.PilotName,
            AppearanceName = model.ShipModel.AppearanceName,
            BaseSize = model.Base.Size.ToString(),
            ReviewNotes =
            {
                "This is a deliberately scriptless four-object geometry test.",
                "The base, peg, primary fuselage and visible S-foil configuration remain independent locked objects.",
                "Phase 5G R3 uses the exact T-65 model group observed in the successfully spawned Unified runtime object.",
                "No filename scoring or best-candidate model selection is used.",
                "The closed S-foil mesh is validated and recorded as alternate configuration metadata but is not serialized visibly.",
                "Pilot card, dial, identifier, customizer and runtime configuration Lua remain intentionally excluded."
            }
        };

        var resolver = new ExactRuntimeAssetResolver(unifiedRepositoryPath, result);
        var faction = ResolveFactionTexture(model.Factions);
        const string arc = "front";

        var baseMesh = resolver.Resolve("Base", "Mesh", model.Base.MeshPath);
        var baseDiffuse = resolver.Resolve("Base", "Diffuse", $"assets/ships-v2/bases/{model.Base.RuntimeSize}/{arc}/{faction}.png");
        var baseCollider = resolver.Resolve("Base", "Collider", "assets/colliders/Small_base_Collider.obj");
        var pegMesh = resolver.Resolve("Peg", "Mesh", model.Peg.MeshPath);
        var minisculeCollider = resolver.Resolve("Peg", "Collider", "assets/models/minisculebox.obj");

        var primaryMesh = resolver.Resolve("ShipPrimary", "Mesh", "assets/ships-v2/small/t65xwing/xwingbasev3.obj");
        var openMesh = resolver.Resolve("ShipConfigurationOpen", "Mesh", "assets/ships-v2/small/t65xwing/xwingopenv3.obj");
        var closedMesh = resolver.Resolve("ShipConfigurationClosed", "AlternateMesh", "assets/ships-v2/small/t65xwing/xwingclosedv3.obj");
        var shipTexture = resolver.Resolve("ShipModelGroup", "SharedDiffuse", "assets/ships-v2/small/t65xwing/Textures/2/cavernangels.jpg");
        var shipCollider = resolver.Resolve("ShipModelGroup", "Collider", "assets/models/minisculebox.obj");

        result.Configuration = new FirstEditionConfigurationMetadata
        {
            PrimaryMesh = primaryMesh,
            VisibleConfigurationName = "Open S-foils",
            VisibleConfigurationMesh = openMesh,
            AlternateConfigurationName = "Closed S-foils",
            AlternateConfigurationMesh = closedMesh,
            SharedTexture = shipTexture
        };
        result.AlternateConfigurationRecorded = !string.IsNullOrWhiteSpace(closedMesh);

        var baseObject = BuildObject(prototype, model.ShipId, "base", $"1E Test Base - {model.ShipName}",
            "Phase 5G R3: exact small First Edition base and working runtime collider.", baseMesh, baseDiffuse, baseCollider,
            0f, 1.25f, 0f, 180f, RuntimeScale, 1, 1);

        var pegObject = BuildObject(prototype, model.ShipId, "peg", $"1E Test Peg - {model.ShipName}",
            "Phase 5G R3: exact small peg.", pegMesh, baseDiffuse, minisculeCollider,
            0f, 1.25f, 0f, 180f, RuntimeScale, 1, 1);

        var primaryObject = BuildObject(prototype, model.ShipId, "ship-primary", $"{model.ShipName} - Primary Fuselage",
            "Phase 5G R3: runtime-derived T-65 primary fuselage model.", primaryMesh, shipTexture, shipCollider,
            0f, ShipY, 0f, 180f, RuntimeScale, 1, 1);

        var openConfigurationObject = BuildObject(prototype, model.ShipId, "ship-config-open", $"{model.ShipName} - Open S-Foils",
            "Phase 5G R3: runtime-derived visible open S-foil configuration model.", openMesh, shipTexture, shipCollider,
            0f, ShipY, 0f, 180f, RuntimeScale, 1, 1);

        var states = new JsonArray(baseObject, pegObject, primaryObject, openConfigurationObject);
        root["SaveName"] = $"X-Wing Unified First Edition - Phase 5G R3 - {model.ShipName}";
        root["GameMode"] = "";
        root["EpochTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        root["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt");
        root["Note"] = "Phase 5G R3 scriptless physical assembly test using the exact runtime-derived T-65 model group.";
        root["LuaScript"] = "";
        root["LuaScriptState"] = "";
        root["ObjectStates"] = states;

        Directory.CreateDirectory(Path.GetDirectoryName(outputSavePath) ?? ".");
        File.WriteAllText(outputSavePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        result.Objects.Add(Record("Base", baseObject));
        result.Objects.Add(Record("Peg", pegObject));
        result.Objects.Add(Record("ShipPrimary", primaryObject));
        result.Objects.Add(Record("ShipConfigurationOpen", openConfigurationObject));
        result.ObjectCount = result.Objects.Count;
        result.BaseSerialized = result.Objects.Any(x => x.Component == "Base");
        result.PegSerialized = result.Objects.Any(x => x.Component == "Peg");
        result.PrimaryShipModelSerialized = result.Objects.Any(x => x.Component == "ShipPrimary");
        result.VisibleConfigurationSerialized = result.Objects.Any(x => x.Component == "ShipConfigurationOpen");
        result.GlobalLuaRemoved = (root["LuaScript"]?.GetValue<string>() ?? "") == "";
        result.ObjectScriptsRemoved = states.OfType<JsonObject>().All(IsScriptless);

        foreach (var unresolved in result.AssetResolutions.Where(x => !x.Accepted))
            result.ValidationErrors.Add($"{unresolved.Component}/{unresolved.Role}: {unresolved.Reason}");

        result.ReadyForTtsLoadTest = result.ObjectCount == 4 && result.BaseSerialized && result.PegSerialized &&
            result.PrimaryShipModelSerialized && result.VisibleConfigurationSerialized && result.AlternateConfigurationRecorded &&
            result.GlobalLuaRemoved && result.ObjectScriptsRemoved && result.ValidationErrors.Count == 0;
        return result;
    }

    private static void ValidateModel(FirstEditionShipObjectModel model)
    {
        if (model.Base.Size is not (FirstEditionBaseSize.Small or FirstEditionBaseSize.Large or FirstEditionBaseSize.Epic))
            throw new InvalidDataException($"Unsupported First Edition base size '{model.Base.Size}'.");
        if (!model.Base.IsValid || !model.Peg.IsValid || !model.ShipModel.IsValid)
            throw new InvalidDataException("Base, peg and ship-model components must all be valid before serialization.");
    }

    private static void ValidateSupportedRuntimeRecipe(FirstEditionShipObjectModel model)
    {
        var key = new string((model.ShipId + model.ShipName).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (!key.Contains("xwing") && !key.Contains("t65"))
            throw new InvalidDataException("Phase 5G R3 is intentionally limited to the runtime-derived T-65 X-Wing recipe.");
        if (model.Base.Size != FirstEditionBaseSize.Small)
            throw new InvalidDataException("The runtime-derived T-65 recipe requires a Small First Edition base.");
    }

    private sealed class ExactRuntimeAssetResolver
    {
        private readonly string _repo;
        private readonly FirstEditionShipTestSaveResult _result;

        public ExactRuntimeAssetResolver(string repo, FirstEditionShipTestSaveResult result)
        {
            _repo = repo;
            _result = result;
        }

        public string Resolve(string component, string role, string relativePath)
        {
            var normalized = NormalizeRelative(relativePath);
            var local = Path.Combine(_repo, normalized.Replace('/', Path.DirectorySeparatorChar));
            var exists = File.Exists(local);
            var url = VerifyCache + GitHubRawPrefix + Uri.EscapeDataString(normalized).Replace("%2F", "/");
            _result.AssetResolutions.Add(new FirstEditionAssetResolutionRecord
            {
                Component = component,
                Role = role,
                OriginalValue = relativePath,
                ResolvedValue = url,
                Source = "Exact Unified runtime-derived repository path",
                LocalPath = local,
                Accepted = exists,
                Reason = exists
                    ? "Exact repository file exists locally and matches the live spawned T-65 assembly recipe."
                    : "Required exact runtime-derived repository file does not exist locally."
            });
            return url;
        }

        private static string NormalizeRelative(string path)
        {
            var value = path.Replace('\\', '/').TrimStart('/');
            var marker = "assets/";
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? value[index..] : value;
        }
    }

    private static JsonObject BuildObject(JsonObject prototype, string shipId, string component, string nickname,
        string description, string meshUrl, string diffuseUrl, string colliderUrl,
        float x, float y, float z, float rotY, float scale, int materialIndex, int typeIndex, string normalUrl = "")
    {
        var obj = Clone(prototype);
        obj["GUID"] = StableGuid($"phase5g-r3|{shipId}|{component}");
        obj["Name"] = "Custom_Model";
        obj["Nickname"] = nickname;
        obj["Description"] = description;
        obj["GMNotes"] = JsonSerializer.Serialize(new { phase = "5G-R3", shipId, component, scriptless = true });
        obj["Locked"] = true;
        obj["DragSelectable"] = false;
        obj["Autoraise"] = false;
        obj["Sticky"] = false;
        obj["Tooltip"] = true;
        obj["LuaScript"] = "";
        obj["LuaScriptState"] = "";
        obj["XmlUI"] = "";
        obj["CustomUIAssets"] = new JsonArray();
        obj.Remove("AttachedObjects");
        obj.Remove("ContainedObjects");
        obj.Remove("States");
        obj["Transform"] = new JsonObject
        {
            ["posX"] = x, ["posY"] = y, ["posZ"] = z,
            ["rotX"] = 0f, ["rotY"] = rotY, ["rotZ"] = 0f,
            ["scaleX"] = scale, ["scaleY"] = scale, ["scaleZ"] = scale
        };
        obj["ColorDiffuse"] = new JsonObject { ["r"] = 1f, ["g"] = 1f, ["b"] = 1f };
        obj["CustomMesh"] = new JsonObject
        {
            ["MeshURL"] = meshUrl,
            ["DiffuseURL"] = diffuseUrl,
            ["NormalURL"] = normalUrl,
            ["ColliderURL"] = colliderUrl,
            ["Convex"] = true,
            ["MaterialIndex"] = materialIndex,
            ["TypeIndex"] = typeIndex,
            ["CustomShader"] = new JsonObject
            {
                ["SpecularColor"] = new JsonObject { ["r"] = 1f, ["g"] = 1f, ["b"] = 1f },
                ["SpecularIntensity"] = 0f,
                ["SpecularSharpness"] = 2f,
                ["FresnelStrength"] = 0f
            },
            ["CastShadows"] = true
        };
        return obj;
    }

    private static FirstEditionSerializedObjectRecord Record(string component, JsonObject obj)
    {
        var t = obj["Transform"]!.AsObject();
        var mesh = obj["CustomMesh"]!.AsObject();
        return new FirstEditionSerializedObjectRecord
        {
            Component = component,
            Guid = obj["GUID"]?.GetValue<string>() ?? "",
            Nickname = obj["Nickname"]?.GetValue<string>() ?? "",
            MeshUrl = mesh["MeshURL"]?.GetValue<string>() ?? "",
            DiffuseUrl = mesh["DiffuseURL"]?.GetValue<string>() ?? "",
            ColliderUrl = mesh["ColliderURL"]?.GetValue<string>() ?? "",
            PositionX = t["posX"]?.GetValue<float>() ?? 0,
            PositionY = t["posY"]?.GetValue<float>() ?? 0,
            PositionZ = t["posZ"]?.GetValue<float>() ?? 0,
            RotationY = t["rotY"]?.GetValue<float>() ?? 0,
            Scale = t["scaleX"]?.GetValue<float>() ?? 0
        };
    }

    private static bool IsScriptless(JsonObject obj) =>
        string.IsNullOrEmpty(obj["LuaScript"]?.GetValue<string>()) &&
        string.IsNullOrEmpty(obj["LuaScriptState"]?.GetValue<string>()) &&
        string.IsNullOrEmpty(obj["XmlUI"]?.GetValue<string>());

    private static JsonObject? FindObjectByGuid(JsonNode? node, string guid)
    {
        if (node is JsonObject obj)
        {
            if (string.Equals(obj["GUID"]?.GetValue<string>(), guid, StringComparison.OrdinalIgnoreCase)) return obj;
            foreach (var value in obj.Select(x => x.Value))
            {
                var found = FindObjectByGuid(value, guid);
                if (found is not null) return found;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var found = FindObjectByGuid(item, guid);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static JsonObject Clone(JsonNode node) => JsonNode.Parse(node.ToJsonString())?.AsObject()
        ?? throw new InvalidDataException("Could not clone the composite base prototype.");

    private static string ResolveFactionTexture(IEnumerable<string> factions)
    {
        foreach (var faction in factions)
        {
            var value = new string(faction.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            if (value.Contains("rebel")) return "rebel";
            if (value.Contains("empire") || value.Contains("imperial")) return "empire";
            if (value.Contains("scum")) return "scum";
            if (value.Contains("republic")) return "republic";
            if (value.Contains("separatist")) return "separatist";
            if (value.Contains("resistance")) return "resistance";
            if (value.Contains("firstorder")) return "firstorder";
        }
        return "rebel";
    }

    private static string StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..6];
    }
}
