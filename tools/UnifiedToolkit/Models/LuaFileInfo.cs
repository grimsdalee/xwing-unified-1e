namespace UnifiedToolkit.Models;

public sealed class LuaFileInfo
{
    public string Path { get; init; } = "";
    public string Folder { get; init; } = "";
    public int LineCount { get; init; }

    public List<string> Functions { get; } = new();
    public List<string> Requires { get; } = new();

    public bool UsesSpawnObject { get; init; }
    public bool UsesSpawnObjectData { get; init; }
    public bool UsesTakeObject { get; init; }
    public bool UsesCreateButton { get; init; }
    public bool UsesJsonDecode { get; init; }
    public bool UsesJsonEncode { get; init; }
}