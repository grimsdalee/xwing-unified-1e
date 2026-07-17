namespace UnifiedToolkit.Runtime;

public sealed class ShipConstructionRecipeReport
{
    public string SourceSave { get; set; } = "";
    public string ModuleName { get; set; } = "Game.Component.Spawner.Spawner";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public List<string> RequestedFunctions { get; set; } = new();
    public List<ShipConstructionFunctionRecipe> Functions { get; set; } = new();
    public List<ShipConstructionDependency> Dependencies { get; set; } = new();
    public List<string> Findings { get; set; } = new();
}

public sealed class ShipConstructionFunctionRecipe
{
    public string Name { get; set; } = "";
    public bool Found { get; set; }
    public int CharacterOffset { get; set; }
    public int CharacterLength { get; set; }
    public string SourceFile { get; set; } = "";
    public List<string> Calls { get; set; } = new();
    public List<string> ReferencedSymbols { get; set; } = new();
    public List<string> StringLiterals { get; set; } = new();
    public List<string> UrlLiterals { get; set; } = new();
    public List<string> GuidSymbols { get; set; } = new();
}

public sealed class ShipConstructionDependency
{
    public string Symbol { get; set; } = "";
    public string Guid { get; set; } = "";
    public bool Resolved { get; set; }
    public string ObjectName { get; set; } = "";
    public string ObjectNickname { get; set; } = "";
    public string ObjectPath { get; set; } = "";
    public List<RuntimeAssetRecord> Assets { get; set; } = new();
    public List<string> UsedByFunctions { get; set; } = new();
}
