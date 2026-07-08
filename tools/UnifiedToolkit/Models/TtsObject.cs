using System.Text.Json.Nodes;

namespace UnifiedToolkit.Models;

public sealed class TtsObject
{
    public JsonObject Json { get; init; } = new();

    public TtsObject? Parent { get; set; }

    public string Guid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string Description { get; init; } = "";
    public string GMNotes { get; init; } = "";
    public string Type { get; init; } = "";

    public bool HasLua { get; init; }
    public bool HasXml { get; init; }

    public List<TtsObject> Children { get; } = new();
}