namespace UnifiedToolkit.Assets;

public sealed class AssetRecord
{
    public string AssetId { get; init; } = "";
    public AssetKind Kind { get; init; }
    public AssetStructuralClass StructuralClass { get; init; }
    public AssetSourceKind SourceKind { get; init; }
    public string Name { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Url { get; init; } = "";
    public string TtsGuid { get; init; } = "";
    public string TtsType { get; init; } = "";
    public string ParentGuid { get; init; } = "";
    public string JsonPointer { get; init; } = "";
    public string ChassisContext { get; init; } = "";
    public string FactionContext { get; init; } = "";
    public string SizeContext { get; init; } = "";
    public string TemplateJson { get; init; } = "";
    public IReadOnlyList<string> SearchTerms { get; init; } = Array.Empty<string>();
}
