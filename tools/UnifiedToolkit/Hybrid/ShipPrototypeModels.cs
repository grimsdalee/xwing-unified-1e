using System.Text.Json.Nodes;

namespace UnifiedToolkit.Hybrid;

public sealed class ShipPrototypeBuildDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public string GeneratedUtc { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public string HybridDefinitionPath { get; init; } = "";
    public string UnifiedSavePath { get; init; } = "";
    public ShipPrototypeBuildSummary Summary { get; init; } = new();
    public IReadOnlyList<ShipPrototypeBuildResult> Prototypes { get; init; } = Array.Empty<ShipPrototypeBuildResult>();
}

public sealed class ShipPrototypeBuildSummary
{
    public int RequestedShipCount { get; init; }
    public int GeneratedPrototypeCount { get; init; }
    public int FailedPrototypeCount { get; init; }
    public bool T65XWingGenerated { get; init; }
    public bool Arc170Generated { get; init; }
}

public sealed class ShipPrototypeBuildResult
{
    public string ShipId { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string FirstEditionBaseSize { get; init; } = "";
    public string Source25BaseSize { get; init; } = "";
    public bool MediumRemoved { get; init; }
    public string AppearanceName { get; init; } = "";
    public string AppearanceVariantId { get; init; } = "";
    public string MeshUrl { get; init; } = "";
    public string DiffuseUrl { get; init; } = "";
    public string OutputFile { get; init; } = "";
    public bool Generated { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class ShipPrototypeObject
{
    public required JsonObject ObjectJson { get; init; }
    public required ShipPrototypeBuildResult Result { get; init; }
}
