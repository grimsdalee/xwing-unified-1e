using System.Text;
using System.Text.Json.Nodes;
using UnifiedToolkit.Models;

namespace UnifiedToolkit.TTS;

public static class TtsSaveLoader
{
    public static TtsGame Load(string path)
    {
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"TTS JSON file not found: {path}", path);

        var jsonText = File.ReadAllText(path, Encoding.UTF8);
        var root = JsonNode.Parse(jsonText)?.AsObject();

        if (root is null)
            throw new InvalidOperationException("Could not parse TTS JSON root object.");

        var game = new TtsGame
        {
            SourcePath = path,
            GlobalLua = TtsJsonHelpers.GetString(root, "LuaScript"),
            GlobalXml = TtsJsonHelpers.GetString(root, "XmlUI")
        };

        if (root["ObjectStates"] is JsonArray objects)
        {
            foreach (var node in objects)
            {
                if (node is JsonObject obj)
                    game.Objects.Add(ReadObject(obj, parent: null));
            }
        }

        return game;
    }

    private static TtsObject ReadObject(JsonObject json, TtsObject? parent)
    {
        var result = new TtsObject
        {
            Json = json,
            Parent = parent,

            Guid = TtsJsonHelpers.GetString(json, "GUID"),
            Name = TtsJsonHelpers.GetString(json, "Name"),
            Nickname = TtsJsonHelpers.GetString(json, "Nickname"),
            Description = TtsJsonHelpers.GetString(json, "Description"),
            GMNotes = TtsJsonHelpers.GetString(json, "GMNotes"),
            Type = TtsJsonHelpers.GetString(json, "Name"),

            HasLua = !string.IsNullOrWhiteSpace(TtsJsonHelpers.GetString(json, "LuaScript")),
            HasXml = !string.IsNullOrWhiteSpace(TtsJsonHelpers.GetString(json, "XmlUI"))
        };

        if (json["ContainedObjects"] is JsonArray children)
        {
            foreach (var childNode in children)
            {
                if (childNode is JsonObject childObj)
                    result.Children.Add(ReadObject(childObj, result));
            }
        }

        return result;
    }
}