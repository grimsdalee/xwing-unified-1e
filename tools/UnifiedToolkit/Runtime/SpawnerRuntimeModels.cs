namespace UnifiedToolkit.Runtime;

public sealed class SpawnerRuntimeReport
{
    public string SourceSave { get; set; } = "";
    public string ModuleName { get; set; } = "Game.Component.Spawner.Spawner";
    public RuntimeSummary Summary { get; set; } = new();
    public List<RuntimeObjectRecord> Objects { get; set; } = new();
    public List<RuntimeGuidReference> GuidReferences { get; set; } = new();
    public List<RuntimeFunctionRecord> Functions { get; set; } = new();
    public List<string> Findings { get; set; } = new();
}

public sealed class RuntimeSummary
{
    public int TotalObjects { get; set; }
    public int ObjectsWithLua { get; set; }
    public int ObjectsWithCustomAssets { get; set; }
    public int TotalAssetUrls { get; set; }
    public int ModuleCharacters { get; set; }
    public int FunctionCount { get; set; }
    public int GuidReferenceCount { get; set; }
    public int ResolvedGuidCount { get; set; }
    public int UnresolvedGuidCount { get; set; }
}

public sealed class RuntimeObjectRecord
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public bool HasLua { get; set; }
    public bool HasXml { get; set; }
    public List<RuntimeAssetRecord> Assets { get; set; } = new();
}

public sealed class RuntimeAssetRecord
{
    public string Container { get; set; } = "";
    public string Role { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class RuntimeGuidReference
{
    public string Symbol { get; set; } = "";
    public string Guid { get; set; } = "";
    public string ReferenceKind { get; set; } = "";
    public bool UsedBySpawnerModule { get; set; }
    public bool Resolved { get; set; }
    public string ObjectName { get; set; } = "";
    public string ObjectNickname { get; set; } = "";
    public string ObjectPath { get; set; } = "";
    public List<RuntimeAssetRecord> Assets { get; set; } = new();
}

public sealed class RuntimeFunctionRecord
{
    public string Name { get; set; } = "";
    public int CharacterOffset { get; set; }
}
