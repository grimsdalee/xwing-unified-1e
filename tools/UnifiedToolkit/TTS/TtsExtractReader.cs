using System.Text;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.TTS;

public static class TtsExtractReader
{
    public static List<TtsExtractedObject> ReadObjects(string extractFolder)
    {
        var objectsRoot = Path.Combine(extractFolder, "objects");

        if (!Directory.Exists(objectsRoot))
            throw new DirectoryNotFoundException($"Objects folder not found: {objectsRoot}");

        var result = new List<TtsExtractedObject>();

        foreach (var objectJsonPath in Directory.GetFiles(objectsRoot, "object.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(objectJsonPath, Encoding.UTF8);
            var obj = JsonNode.Parse(json)?.AsObject();

            if (obj is null)
                continue;

            result.Add(new TtsExtractedObject
            {
                Folder = Path.GetDirectoryName(objectJsonPath)!,
                ObjectJsonPath = objectJsonPath,
                Json = obj
            });
        }

        return result
            .OrderBy(x => x.Folder)
            .ToList();
    }
}