using System.Text.Json.Nodes;

namespace TtsExplorer;

public sealed class TtsObjectInfo
{
    public string Guid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string Description { get; init; } = "";
    public string Path { get; init; } = "";
    public string MeshUrl { get; init; } = "";
    public string DiffuseUrl { get; init; } = "";
    public string ColliderUrl { get; init; } = "";
    public int LuaLength { get; init; }
    public int XmlLength { get; init; }
    public JsonObject Source { get; init; } = new();

    public string DisplayName
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Nickname) ? Name : Nickname;
            if (string.IsNullOrWhiteSpace(label)) label = "(unnamed)";
            return $"{label} [{Guid}]";
        }
    }
    
    public string SearchText { get; init; } = "";
}
