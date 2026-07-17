using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnifiedToolkit.Models;

namespace UnifiedToolkit.Hybrid;

public static class PrototypeShipObjectBuilder
{
    private const string SpawnPrefix = "{verifycache}https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/ships-v2/";
    private const string PegCollider = "{verifycache}https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/models/minisculebox.obj";

    public static ShipPrototypeObject Build(
        HybridShipDefinition ship,
        ShipAppearanceVariant appearance,
        TtsObject compositeBase,
        string outputFile,
        float positionX)
    {
        if (!ship.Readiness.ReadyForObjectBuilder)
            throw new InvalidOperationException($"Ship '{ship.SemanticData.Id}' is not ready for Object Builder generation.");
        if (string.IsNullOrWhiteSpace(appearance.MeshUrl))
            throw new InvalidDataException($"Appearance '{appearance.DisplayName}' has no mesh URL.");

        var root = CloneObject(compositeBase.Json);
        var recipeSize = ResolveRecipeSize(ship.BaseDefinition.Size);
        var factionTexture = ResolveFactionTexture(ship.SemanticData.Factions);
        var modelScale = ReadLegacyScale(appearance.TemplateJson);
        var modelRotationY = ReadLegacyRotationY(appearance.TemplateJson);

        root["GUID"] = StableGuid(ship.SemanticData.Id + "|" + appearance.VariantId + "|base");
        root["Nickname"] = $"1E Prototype Base - {ship.SemanticData.Name}";
        root["Description"] = $"Static Phase 5B R3 visual prototype base. First Edition size: {ship.BaseDefinition.Size}.";
        root["GMNotes"] = JsonSerializer.Serialize(new
        {
            schema = "xwing-unified-1e-prototype-1.1",
            shipId = ship.SemanticData.Id,
            shipName = ship.SemanticData.Name,
            firstEditionBaseSize = ship.BaseDefinition.Size.ToString(),
            source25BaseSize = ship.BaseSizeConversion?.Source25BaseSize ?? "",
            mediumRemoved = ship.BaseSizeConversion?.MediumRemoved ?? false,
            appearanceVariantId = appearance.VariantId,
            appearanceName = appearance.DisplayName,
            sourceGuid = appearance.SourceGuid,
            modelScale,
            modelRotationY,
            normalUrl = appearance.NormalUrl,
            recipeSize,
            factionTexture
        });

        SetTransform(root, positionX, 1.25f, 0f, 0f, 180f, 0f, 1f);
        root["Locked"] = true;
        root["DragSelectable"] = false;

        var mesh = EnsureObject(root, "CustomMesh");
        mesh["MeshURL"] = $"{SpawnPrefix}bases/{recipeSize}/base.obj";
        mesh["DiffuseURL"] = $"{SpawnPrefix}bases/{recipeSize}/front/{factionTexture}.png";
        mesh["NormalURL"] = "";
        mesh["ColliderURL"] = "";
        mesh["Convex"] = true;
        mesh["MaterialIndex"] = 3;
        mesh["TypeIndex"] = 0;

        MakeStaticAndScriptless(root);

        var warnings = new List<string>
        {
            "This R3 save contains independent static base, peg and model objects.",
            "No Lua, XML, runtime spawning or attachment behaviour is used.",
            "The purpose of R3 is to prove TTS JSON loading and validate visual transforms only."
        };
        if (ship.BaseSizeConversion?.MediumRemoved == true)
            warnings.Add($"The source 2.5 {ship.BaseSizeConversion.Source25BaseSize} model is mounted over the authoritative First Edition {ship.BaseDefinition.Size} base.");

        return new ShipPrototypeObject
        {
            ObjectJson = root,
            Result = new ShipPrototypeBuildResult
            {
                ShipId = ship.SemanticData.Id,
                ShipName = ship.SemanticData.Name,
                FirstEditionBaseSize = ship.BaseDefinition.Size.ToString(),
                Source25BaseSize = ship.BaseSizeConversion?.Source25BaseSize ?? "",
                MediumRemoved = ship.BaseSizeConversion?.MediumRemoved ?? false,
                AppearanceName = appearance.DisplayName,
                AppearanceVariantId = appearance.VariantId,
                MeshUrl = appearance.MeshUrl,
                DiffuseUrl = appearance.DiffuseUrl,
                OutputFile = outputFile,
                Generated = true,
                Warnings = warnings
            }
        };
    }

    public static JsonObject BuildPrototypeSave(
        IReadOnlyList<ShipPrototypeObject> prototypes,
        JsonObject unifiedSaveRoot)
    {
        var save = CloneObject(unifiedSaveRoot);
        var states = new JsonArray();

        foreach (var prototype in prototypes)
        {
            var metadata = ParseMetadata(prototype.ObjectJson);
            var baseObject = CloneObject(prototype.ObjectJson);
            var baseTransform = EnsureObject(baseObject, "Transform");
            var x = ReadFloat(baseTransform, "posX", 0f);
            var z = ReadFloat(baseTransform, "posZ", 0f);

            states.Add(baseObject);
            states.Add(BuildPegObject(prototype, metadata, x, z));
            states.Add(BuildModelObject(prototype, metadata, x, z));
        }

        save["SaveName"] = "X-Wing Unified First Edition - Phase 5B R3 Static Visual Prototypes";
        save["EpochTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        save["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt");
        save["Note"] = "Static, scriptless visual prototypes. Base, peg and model are independent objects.";

        // Preserve the known-valid save envelope, global XML and UI assets. Only the
        // executable Global Lua is disabled. The prototype objects themselves contain
        // no Lua, XML, UI assets or runtime attachments.
        save["LuaScript"] = "";
        save["LuaScriptState"] = "";
        save["ObjectStates"] = states;
        return save;
    }

    private static JsonObject BuildPegObject(ShipPrototypeObject prototype, PrototypeMetadata metadata, float x, float z)
    {
        var peg = CloneObject(prototype.ObjectJson);
        peg["GUID"] = StableGuid(prototype.Result.ShipId + "|" + prototype.Result.AppearanceVariantId + "|peg");
        peg["Nickname"] = $"1E Prototype Peg - {prototype.Result.ShipName}";
        peg["Description"] = "Static prototype peg; not attached in Phase 5B R3.";
        peg["GMNotes"] = "";
        SetTransform(peg, x, 1.61f, z, 0f, 180f, 0f, 1f);

        var mesh = EnsureObject(peg, "CustomMesh");
        mesh["MeshURL"] = $"{SpawnPrefix}bases/pegs/peg.obj";
        mesh["DiffuseURL"] = $"{SpawnPrefix}bases/{metadata.RecipeSize}/front/{metadata.FactionTexture}.png";
        mesh["NormalURL"] = "";
        mesh["ColliderURL"] = PegCollider;
        mesh["Convex"] = true;
        mesh["MaterialIndex"] = 1;
        mesh["TypeIndex"] = 0;
        MakeStaticAndScriptless(peg);
        return peg;
    }

    private static JsonObject BuildModelObject(ShipPrototypeObject prototype, PrototypeMetadata metadata, float x, float z)
    {
        var model = CloneObject(prototype.ObjectJson);
        model["GUID"] = StableGuid(prototype.Result.ShipId + "|" + prototype.Result.AppearanceVariantId + "|model");
        model["Nickname"] = $"{prototype.Result.ShipName} - {prototype.Result.AppearanceName}";
        model["Description"] = "Static prototype ship model; not attached in Phase 5B R3.";
        model["GMNotes"] = "";
        SetTransform(model, x, 3.45f, z, 0f, 180f + metadata.ModelRotationY, 0f, metadata.ModelScale);

        var mesh = EnsureObject(model, "CustomMesh");
        mesh["MeshURL"] = prototype.Result.MeshUrl;
        mesh["DiffuseURL"] = prototype.Result.DiffuseUrl;
        mesh["NormalURL"] = metadata.NormalUrl;
        mesh["ColliderURL"] = PegCollider;
        mesh["Convex"] = true;
        mesh["MaterialIndex"] = 1;
        mesh["TypeIndex"] = 0;
        MakeStaticAndScriptless(model);
        return model;
    }

    private static PrototypeMetadata ParseMetadata(JsonObject root)
    {
        try
        {
            var notes = root["GMNotes"]?.GetValue<string>() ?? "{}";
            var data = JsonNode.Parse(notes)?.AsObject();
            return new PrototypeMetadata(
                data?["modelScale"]?.GetValue<float>() ?? 0.625f,
                data?["modelRotationY"]?.GetValue<float>() ?? 0f,
                data?["normalUrl"]?.GetValue<string>() ?? "",
                data?["recipeSize"]?.GetValue<string>() ?? "small",
                data?["factionTexture"]?.GetValue<string>() ?? "rebel");
        }
        catch
        {
            return new PrototypeMetadata(0.625f, 0f, "", "small", "rebel");
        }
    }

    private static void MakeStaticAndScriptless(JsonObject root)
    {
        root["Locked"] = true;
        root["DragSelectable"] = false;
        root["LuaScript"] = "";
        root["LuaScriptState"] = "";
        root["XmlUI"] = "";
        root["CustomUIAssets"] = new JsonArray();
        root.Remove("AttachedObjects");
        root.Remove("ContainedObjects");
        root.Remove("States");
    }

    private static string ResolveRecipeSize(FirstEditionBaseSize size) => size switch
    {
        FirstEditionBaseSize.Small => "small",
        FirstEditionBaseSize.Large => "large",
        FirstEditionBaseSize.Epic => "huge",
        _ => throw new InvalidDataException($"Unsupported First Edition base size '{size}'.")
    };

    private static float ReadLegacyScale(string templateJson)
    {
        try { return JsonNode.Parse(templateJson)?["Transform"]?["scaleX"]?.GetValue<float>() ?? 0.625f; }
        catch { return 0.625f; }
    }

    private static float ReadLegacyRotationY(string templateJson)
    {
        try
        {
            var rotation = JsonNode.Parse(templateJson)?["Transform"]?["rotY"]?.GetValue<float>() ?? 0f;
            var normalized = rotation % 360f;
            return Math.Abs(normalized - 360f) < 0.1f ? 0f : normalized;
        }
        catch { return 0f; }
    }

    private static JsonObject CloneObject(JsonNode node) =>
        JsonNode.Parse(node.ToJsonString())?.AsObject()
        ?? throw new InvalidDataException("Could not clone JSON object.");

    private static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing) return existing;
        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static void SetTransform(JsonObject root, float x, float y, float z, float rotX, float rotY, float rotZ, float scale)
    {
        var transform = EnsureObject(root, "Transform");
        transform["posX"] = x; transform["posY"] = y; transform["posZ"] = z;
        transform["rotX"] = rotX; transform["rotY"] = rotY; transform["rotZ"] = rotZ;
        transform["scaleX"] = scale; transform["scaleY"] = scale; transform["scaleZ"] = scale;
    }

    private static float ReadFloat(JsonObject root, string name, float fallback)
    {
        try { return root[name]?.GetValue<float>() ?? fallback; }
        catch { return fallback; }
    }

    private static string ResolveFactionTexture(IReadOnlyList<string> factions)
    {
        foreach (var faction in factions)
        {
            var normalized = new string(faction.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            if (normalized.Contains("rebel")) return "rebel";
            if (normalized.Contains("empire") || normalized.Contains("imperial")) return "empire";
            if (normalized.Contains("scum")) return "scum";
        }
        return "rebel";
    }

    private static string StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..6];
    }

    private sealed record PrototypeMetadata(float ModelScale, float ModelRotationY, string NormalUrl, string RecipeSize, string FactionTexture);
}
