using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime;

public sealed class RuntimeShipPrototypeDocument
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string SourceSavePath { get; set; } = "";
    public string RequestedGuid { get; set; } = "";
    public RuntimeShipPrototypeSummary Summary { get; set; } = new();
    public RuntimeShipPrototype? Prototype { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> ReviewNotes { get; set; } = new();
}

public sealed class RuntimeShipPrototypeSummary
{
    public bool ObjectFound { get; set; }
    public bool IsShipObject { get; set; }
    public bool LuaStateParsed { get; set; }
    public bool VisualDefinitionAvailable { get; set; }
    public bool PrimaryMeshAvailable { get; set; }
    public bool ActiveConfigurationAvailable { get; set; }
    public int ChildObjectCount { get; set; }
    public int ConfigurationCount { get; set; }
    public int TextureCount { get; set; }
    public int ScriptCharacterCount { get; set; }
    public int ErrorCount { get; set; }
    public bool ReadyForPrototypeCloning { get; set; }
}

public sealed class RuntimeShipPrototype
{
    public string Guid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string ShipId { get; set; } = "";
    public string PilotXws { get; set; } = "";
    public string Faction { get; set; } = "";
    public string RuntimeSize { get; set; } = "";
    public RuntimeTransformDefinition Transform { get; set; } = new();
    public ShipVisualDefinition Visual { get; set; } = new();
    public List<RuntimePrototypeChildDefinition> Children { get; set; } = new();
    public JsonObject ShipData { get; set; } = new();
    public JsonObject UiData { get; set; } = new();
    public string LuaScript { get; set; } = "";
    public string XmlUi { get; set; } = "";
    public JsonObject SourceObjectSnapshot { get; set; } = new();
}

public sealed class ShipVisualDefinition
{
    public string BaseMesh { get; set; } = "";
    public string BaseDiffuse { get; set; } = "";
    public string BaseCollider { get; set; } = "";
    public string PrimaryMesh { get; set; } = "";
    public string SelectedTextureKey { get; set; } = "";
    public string SelectedTextureUrl { get; set; } = "";
    public Dictionary<string, string> Textures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ShipVisualConfigurationDefinition> Configurations { get; set; } = new();
    public int ActiveConfigurationIndex { get; set; } = 1;
}

public sealed class ShipVisualConfigurationDefinition
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string ContextText { get; set; } = "";
    public string Message { get; set; } = "";
    public string Mesh { get; set; } = "";
    public double ZRotation { get; set; }
    public bool IsActive { get; set; }
}

public sealed class RuntimePrototypeChildDefinition
{
    public int Index { get; set; }
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Description { get; set; } = "";
    public string InferredRole { get; set; } = "Unknown";
    public RuntimeTransformDefinition Transform { get; set; } = new();
    public string MeshUrl { get; set; } = "";
    public string DiffuseUrl { get; set; } = "";
    public string ColliderUrl { get; set; } = "";
    public bool VisibleByScale { get; set; }
    public JsonObject SourceSnapshot { get; set; } = new();
}

public sealed class RuntimeTransformDefinition
{
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double RotationX { get; set; }
    public double RotationY { get; set; }
    public double RotationZ { get; set; }
    public double ScaleX { get; set; }
    public double ScaleY { get; set; }
    public double ScaleZ { get; set; }
}
