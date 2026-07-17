using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime;

public static class RuntimeShipPrototypeCapture
{
    public static RuntimeShipPrototypeDocument Capture(string savePath, string guid)
    {
        var document = new RuntimeShipPrototypeDocument
        {
            SourceSavePath = Path.GetFullPath(savePath),
            RequestedGuid = guid.Trim().ToLowerInvariant()
        };

        var root = JsonNode.Parse(File.ReadAllText(savePath))?.AsObject()
            ?? throw new InvalidDataException("Could not parse the Tabletop Simulator save JSON.");
        var source = FindObjectByGuid(root, guid);
        document.Summary.ObjectFound = source is not null;
        if (source is null)
        {
            document.ValidationErrors.Add($"Object GUID '{guid}' was not found in the save.");
            Finalize(document);
            return document;
        }

        var prototype = new RuntimeShipPrototype
        {
            Guid = Text(source, "GUID"),
            Nickname = Text(source, "Nickname"),
            Transform = ReadTransform(source["Transform"] as JsonObject),
            LuaScript = Text(source, "LuaScript"),
            XmlUi = Text(source, "XmlUI"),
            SourceObjectSnapshot = source.DeepClone().AsObject()
        };
        document.Prototype = prototype;
        document.Summary.ScriptCharacterCount = prototype.LuaScript.Length;
        document.Summary.IsShipObject = HasTag(source, "Ship") || prototype.LuaScript.Contains("__XW_Ship", StringComparison.Ordinal);
        if (!document.Summary.IsShipObject)
            document.ValidationErrors.Add("The selected object does not appear to be a Unified ship base.");

        var stateText = Text(source, "LuaScriptState");
        JsonObject? state = null;
        if (!string.IsNullOrWhiteSpace(stateText))
        {
            try { state = JsonNode.Parse(stateText)?.AsObject(); }
            catch (JsonException ex) { document.ValidationErrors.Add($"LuaScriptState could not be parsed: {ex.Message}"); }
        }
        document.Summary.LuaStateParsed = state is not null;

        if (state is not null)
        {
            prototype.ShipData = (state["shipData"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
            prototype.UiData = (state["uiData"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
            prototype.ShipId = Text(prototype.ShipData, "shipId");
            prototype.PilotXws = Text(prototype.ShipData, "xws");
            prototype.Faction = Text(prototype.ShipData, "Faction");
            prototype.RuntimeSize = Text(prototype.ShipData, "Size");
            BuildVisualDefinition(source, state, prototype.Visual);
        }

        if (source["ChildObjects"] is JsonArray children)
        {
            for (var index = 0; index < children.Count; index++)
            {
                if (children[index] is not JsonObject child) continue;
                prototype.Children.Add(ReadChild(child, index));
            }
        }

        document.Summary.ChildObjectCount = prototype.Children.Count;
        document.Summary.ConfigurationCount = prototype.Visual.Configurations.Count;
        document.Summary.TextureCount = prototype.Visual.Textures.Count;
        document.Summary.PrimaryMeshAvailable = !string.IsNullOrWhiteSpace(prototype.Visual.PrimaryMesh);
        document.Summary.ActiveConfigurationAvailable = prototype.Visual.Configurations.Count == 0 || prototype.Visual.Configurations.Any(x => x.IsActive);
        document.Summary.VisualDefinitionAvailable = !string.IsNullOrWhiteSpace(prototype.Visual.BaseMesh) && document.Summary.PrimaryMeshAvailable;

        if (string.IsNullOrWhiteSpace(prototype.ShipId)) document.ValidationErrors.Add("shipData.shipId is missing.");
        if (string.IsNullOrWhiteSpace(prototype.RuntimeSize)) document.ValidationErrors.Add("shipData.Size is missing.");
        if (string.IsNullOrWhiteSpace(prototype.Visual.PrimaryMesh)) document.ValidationErrors.Add("shipData.mesh is missing.");
        if (string.IsNullOrWhiteSpace(prototype.Visual.SelectedTextureUrl)) document.ValidationErrors.Add("The selected runtime texture could not be resolved.");
        if (prototype.Children.Count == 0) document.ValidationErrors.Add("The selected ship has no saved child objects.");

        document.ReviewNotes.Add("The source object snapshot is preserved so the next phase can clone the working hierarchy rather than reconstructing it.");
        document.ReviewNotes.Add("ShipVisualDefinition separates primary mesh, textures and configuration meshes from pilot/gameplay data.");
        document.ReviewNotes.Add("Transforms are copied exactly from the successfully spawned runtime object.");
        Finalize(document);
        return document;
    }

    private static void BuildVisualDefinition(JsonObject source, JsonObject state, ShipVisualDefinition visual)
    {
        if (source["CustomMesh"] is JsonObject baseMesh)
        {
            visual.BaseMesh = Text(baseMesh, "MeshURL");
            visual.BaseDiffuse = Text(baseMesh, "DiffuseURL");
            visual.BaseCollider = Text(baseMesh, "ColliderURL");
        }

        var shipData = state["shipData"] as JsonObject;
        if (shipData is null) return;
        visual.PrimaryMesh = Text(shipData, "mesh");
        visual.SelectedTextureKey = Text(shipData, "texture");
        if (shipData["textures"] is JsonObject textures)
        {
            foreach (var pair in textures)
            {
                var value = pair.Value?.GetValue<string>() ?? "";
                visual.Textures[pair.Key] = value;
            }
        }
        visual.SelectedTextureUrl = visual.Textures.GetValueOrDefault(visual.SelectedTextureKey, "");

        var activeConfig = Int(state, "current_config", 1);
        visual.ActiveConfigurationIndex = activeConfig;
        if (shipData["Config"] is not JsonObject config) return;
        var context = Text(config, "ContextText");
        if (config["States"] is not JsonArray states) return;
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i] is not JsonObject item) continue;
            visual.Configurations.Add(new ShipVisualConfigurationDefinition
            {
                Index = i + 1,
                Name = Text(item, "Name"),
                ContextText = context,
                Message = Text(item, "Message"),
                Mesh = Text(item, "Model"),
                ZRotation = Number(item, "ZRot"),
                IsActive = i + 1 == activeConfig
            });
        }
    }

    private static RuntimePrototypeChildDefinition ReadChild(JsonObject child, int index)
    {
        var mesh = child["CustomMesh"] as JsonObject;
        var role = InferRole(child, mesh);
        var transform = ReadTransform(child["Transform"] as JsonObject);
        return new RuntimePrototypeChildDefinition
        {
            Index = index,
            Guid = Text(child, "GUID"),
            Name = Text(child, "Name"),
            Nickname = Text(child, "Nickname"),
            Description = Text(child, "Description"),
            InferredRole = role,
            Transform = transform,
            MeshUrl = mesh is null ? "" : Text(mesh, "MeshURL"),
            DiffuseUrl = mesh is null ? "" : Text(mesh, "DiffuseURL"),
            ColliderUrl = mesh is null ? "" : Text(mesh, "ColliderURL"),
            VisibleByScale = Math.Max(Math.Abs(transform.ScaleX), Math.Max(Math.Abs(transform.ScaleY), Math.Abs(transform.ScaleZ))) > 0.01,
            SourceSnapshot = child.DeepClone().AsObject()
        };
    }

    private static string InferRole(JsonObject child, JsonObject? mesh)
    {
        var nickname = Text(child, "Nickname");
        var description = Text(child, "Description");
        var path = mesh is null ? "" : Text(mesh, "MeshURL");
        var combined = $"{nickname} {description} {path}".ToLowerInvariant();
        if (combined.Contains("peg")) return "Peg";
        if (nickname.Equals("Ship", StringComparison.OrdinalIgnoreCase) || combined.Contains("xwingbase")) return "PrimaryShipModel";
        if (nickname.Equals("Config", StringComparison.OrdinalIgnoreCase) || combined.Contains("xwingopen") || combined.Contains("xwingclosed")) return "ConfigurationModel";
        if (combined.Contains("colorid") || combined.Contains("identifier")) return "ShipIdentifier";
        return "Unknown";
    }

    private static JsonObject? FindObjectByGuid(JsonNode? node, string guid)
    {
        if (node is JsonObject obj)
        {
            if (Text(obj, "GUID").Equals(guid, StringComparison.OrdinalIgnoreCase)) return obj;
            foreach (var pair in obj)
            {
                var found = FindObjectByGuid(pair.Value, guid);
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

    private static RuntimeTransformDefinition ReadTransform(JsonObject? value) => new()
    {
        PositionX = Number(value, "posX"), PositionY = Number(value, "posY"), PositionZ = Number(value, "posZ"),
        RotationX = Number(value, "rotX"), RotationY = Number(value, "rotY"), RotationZ = Number(value, "rotZ"),
        ScaleX = Number(value, "scaleX"), ScaleY = Number(value, "scaleY"), ScaleZ = Number(value, "scaleZ")
    };

    private static bool HasTag(JsonObject obj, string tag) => obj["Tags"] is JsonArray tags && tags.Any(x => string.Equals(x?.GetValue<string>(), tag, StringComparison.OrdinalIgnoreCase));
    private static string Text(JsonObject? obj, string property) => obj?[property]?.GetValue<string>() ?? "";
    private static int Int(JsonObject? obj, string property, int fallback) { try { return obj?[property]?.GetValue<int>() ?? fallback; } catch { return fallback; } }
    private static double Number(JsonObject? obj, string property) { try { return obj?[property]?.GetValue<double>() ?? 0; } catch { return 0; } }

    private static void Finalize(RuntimeShipPrototypeDocument document)
    {
        document.Summary.ErrorCount = document.ValidationErrors.Count;
        document.Summary.ReadyForPrototypeCloning = document.Summary.ObjectFound && document.Summary.IsShipObject &&
            document.Summary.LuaStateParsed && document.Summary.VisualDefinitionAvailable &&
            document.Summary.ActiveConfigurationAvailable && document.ValidationErrors.Count == 0;
    }
}
