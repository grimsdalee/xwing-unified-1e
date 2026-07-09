namespace UnifiedToolkit.Models;

public sealed class ModuleInfo
{
    public string Name { get; init; } = "";
    public string Folder { get; init; } = "";

    public List<LuaFileInfo> Files { get; } = new();

    public int FileCount => Files.Count;
    public int TotalLines => Files.Sum(x => x.LineCount);
    public int FunctionCount => Files.Sum(x => x.Functions.Count);
    public int RequireCount => Files.Sum(x => x.Requires.Count);

    public bool UsesSpawnObject => Files.Any(x => x.UsesSpawnObject);
    public bool UsesSpawnObjectData => Files.Any(x => x.UsesSpawnObjectData);
    public bool UsesTakeObject => Files.Any(x => x.UsesTakeObject);
    public bool UsesCreateButton => Files.Any(x => x.UsesCreateButton);
}