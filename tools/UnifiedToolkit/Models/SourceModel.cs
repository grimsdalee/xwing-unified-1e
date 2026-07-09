namespace UnifiedToolkit.Models;

public sealed class SourceModel
{
    public string RepoFolder { get; init; } = "";

    public List<RepoFileEntry> Files { get; } = new();
    public List<LuaFileInfo> LuaFiles { get; } = new();
    public List<ModuleInfo> Modules { get; } = new();
}