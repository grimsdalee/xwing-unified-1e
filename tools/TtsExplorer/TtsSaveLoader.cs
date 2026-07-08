using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TtsExplorer;

public sealed class TtsSaveLoader
{
    public JsonObject? Root { get; private set; }
    public List<TtsObjectInfo> Objects { get; } = new();
    public string GlobalLua { get; private set; } = "";
    public string GlobalXml { get; private set; } = "";
    public string SourcePath { get; private set; } = "";

    public void Load(string path)
    {
        SourcePath = path;
        Objects.Clear();

        var text = File.ReadAllText(path, Encoding.UTF8);
        Root = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException("Could not parse JSON as object.");

        GlobalLua = Root.TryGetPropertyValue("LuaScript", out var luaNode) ? luaNode?.GetValue<string>() ?? "" : "";
        GlobalXml = Root.TryGetPropertyValue("XmlUI", out var xmlNode) ? xmlNode?.GetValue<string>() ?? "" : "";

        if (Root.TryGetPropertyValue("ObjectStates", out var statesNode) && statesNode is JsonArray states)
        {
            for (var i = 0; i < states.Count; i++)
            {
                if (states[i] is JsonObject obj)
                    WalkObject(obj, $"ObjectStates[{i}]");
            }
        }
    }

    private void WalkObject(JsonObject obj, string path)
    {
        var customMesh = obj["CustomMesh"] as JsonObject;

        var info = new TtsObjectInfo
        {
            Guid = GetString(obj, "GUID"),
            Name = GetString(obj, "Name"),
            Nickname = GetString(obj, "Nickname"),
            Description = GetString(obj, "Description"),
            Path = path,
            MeshUrl = customMesh is null ? "" : GetString(customMesh, "MeshURL"),
            DiffuseUrl = customMesh is null ? "" : GetString(customMesh, "DiffuseURL"),
            ColliderUrl = customMesh is null ? "" : GetString(customMesh, "ColliderURL"),
            LuaLength = GetString(obj, "LuaScript").Length,
            XmlLength = GetString(obj, "XmlUI").Length,
            Source = obj
        };

        Objects.Add(info);

        if (obj.TryGetPropertyValue("ContainedObjects", out var containedNode) && containedNode is JsonArray contained)
        {
            for (var i = 0; i < contained.Count; i++)
            {
                if (contained[i] is JsonObject child)
                    WalkObject(child, $"{path}/{info.Guid}[{i}]");
            }
        }
    }

    public static string GetString(JsonObject obj, string name)
    {
        return obj.TryGetPropertyValue(name, out var node) ? node?.GetValue<string>() ?? "" : "";
    }

    public string BuildSummary()
    {
        if (Root is null) return "No save loaded.";

        var topKeys = string.Join(", ", Root.Select(p => p.Key));
        var withLua = Objects.Count(o => o.LuaLength > 0);
        var withXml = Objects.Count(o => o.XmlLength > 0);
        var meshCount = Objects.Count(o => !string.IsNullOrWhiteSpace(o.MeshUrl));
        var steamUrls = Objects.Count(o =>
            o.MeshUrl.Contains("steamusercontent", StringComparison.OrdinalIgnoreCase) ||
            o.DiffuseUrl.Contains("steamusercontent", StringComparison.OrdinalIgnoreCase) ||
            o.ColliderUrl.Contains("steamusercontent", StringComparison.OrdinalIgnoreCase));

        return
$@"Source: {SourcePath}

Top-level keys:
{topKeys}

Objects including contained objects: {Objects.Count}
Objects with Lua: {withLua}
Objects with XML UI: {withXml}
Objects with CustomMesh: {meshCount}
Objects referencing Steam CDN: {steamUrls}

Global Lua length: {GlobalLua.Length:N0}
Global XML UI length: {GlobalXml.Length:N0}";
    }
}
