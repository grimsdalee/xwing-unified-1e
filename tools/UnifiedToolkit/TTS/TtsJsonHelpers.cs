using System.Text.Json.Nodes;

namespace UnifiedToolkit.TTS;

public static class TtsJsonHelpers
{
    public static string GetString(JsonObject obj, string property)
    {
        if (!obj.TryGetPropertyValue(property, out var node) || node is null)
            return "";

        return node.ToJsonString().Trim('"');
    }

    public static string MakeSafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return value.Trim();
    }
}