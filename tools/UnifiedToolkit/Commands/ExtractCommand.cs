using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Commands;

public static class ExtractCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  UnifiedToolkit extract <tts-json-file> [output-folder]");
            return 1;
        }

        var inputPath = Path.GetFullPath(args[0]);

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        var outputRoot = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                Path.GetFileNameWithoutExtension(inputPath) + "_extract");

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "objects"));
        Directory.CreateDirectory(Path.Combine(outputRoot, "scripts"));
        Directory.CreateDirectory(Path.Combine(outputRoot, "xml"));

        Console.WriteLine($"Reading:  {inputPath}");
        Console.WriteLine($"Output:   {outputRoot}");

        var jsonText = File.ReadAllText(inputPath, Encoding.UTF8);
        var root = JsonNode.Parse(jsonText)?.AsObject();

        if (root is null)
        {
            Console.WriteLine("Could not parse TTS JSON.");
            return 1;
        }

        WriteIfPresent(root, "LuaScript", Path.Combine(outputRoot, "scripts", "Global.lua"));
        WriteIfPresent(root, "XmlUI", Path.Combine(outputRoot, "xml", "Global.xml"));

        var index = new List<ObjectIndexEntry>();

        if (root["ObjectStates"] is JsonArray objects)
        {
            for (var i = 0; i < objects.Count; i++)
            {
                if (objects[i] is JsonObject obj)
                    ExtractObject(obj, i, outputRoot, outputRoot, index);
            }
        }

        var indexPath = Path.Combine(outputRoot, "object-index.json");

        File.WriteAllText(
            indexPath,
            JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine($"Extracted objects: {index.Count}");
        Console.WriteLine($"Index written:      {indexPath}");

        return 0;
    }

    private static void ExtractObject(
        JsonObject obj,
        int index,
        string outputRoot,
        string parentFolder,
        List<ObjectIndexEntry> objectIndex)
    {
        var name = TtsJsonHelpers.GetString(obj, "Nickname");
        var guid = TtsJsonHelpers.GetString(obj, "GUID");
        var type = TtsJsonHelpers.GetString(obj, "Name");

        var safeName = TtsJsonHelpers.MakeSafeFileName(
            string.IsNullOrWhiteSpace(name) ? $"object_{index:0000}" : name);

        var id = string.IsNullOrWhiteSpace(guid)
            ? $"{index:0000}_{safeName}"
            : $"{index:0000}_{safeName}_{guid}";

        var objectFolder = Path.Combine(parentFolder, "objects", id);
        Directory.CreateDirectory(objectFolder);

        File.WriteAllText(
            Path.Combine(objectFolder, "object.json"),
            obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        var hasLua = WriteIfPresent(obj, "LuaScript", Path.Combine(objectFolder, "script.lua"));
        var hasXml = WriteIfPresent(obj, "XmlUI", Path.Combine(objectFolder, "ui.xml"));

        objectIndex.Add(new ObjectIndexEntry
        {
            Index = index,
            Guid = guid,
            Name = name,
            Type = type,
            Folder = Path.GetRelativePath(outputRoot, objectFolder),
            HasLua = hasLua,
            HasXml = hasXml,
            Description = TtsJsonHelpers.GetString(obj, "Description"),
            GMNotes = TtsJsonHelpers.GetString(obj, "GMNotes"),
            ContainedCount = obj["ContainedObjects"] is JsonArray containedObjects ? containedObjects.Count : 0,
            CardID = TtsJsonHelpers.GetString(obj, "CardID"),
        });

        if (obj["ContainedObjects"] is JsonArray contained)
        {
            for (var i = 0; i < contained.Count; i++)
            {
                if (contained[i] is JsonObject child)
                    ExtractObject(child, i, outputRoot, objectFolder, objectIndex);
            }
        }
    }

    private static bool WriteIfPresent(JsonObject obj, string property, string path)
    {
        var value = TtsJsonHelpers.GetString(obj, property);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        File.WriteAllText(path, value, Encoding.UTF8);
        return true;
    }
}