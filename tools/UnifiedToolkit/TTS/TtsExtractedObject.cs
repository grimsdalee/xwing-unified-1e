using System.Text.Json.Nodes;

namespace UnifiedToolkit.TTS;

public sealed class TtsExtractedObject
{
    public string Folder { get; set; } = "";
    public string ObjectJsonPath { get; set; } = "";
    public JsonObject Json { get; set; } = new();

    public string Guid => TtsJsonHelpers.GetString(Json, "GUID");
    public string Name => TtsJsonHelpers.GetString(Json, "Nickname");
    public string Description => TtsJsonHelpers.GetString(Json, "Description");
    public string GMNotes => TtsJsonHelpers.GetString(Json, "GMNotes");
    public string Type => TtsJsonHelpers.GetString(Json, "Name");
    public string CardID => TtsJsonHelpers.GetString(Json, "CardID");

    public bool HasLua => File.Exists(Path.Combine(Folder, "script.lua"));
    public bool HasXml => File.Exists(Path.Combine(Folder, "ui.xml"));

    public int ContainedCount =>
        Json["ContainedObjects"] is JsonArray contained ? contained.Count : 0;
}