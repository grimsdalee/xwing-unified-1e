namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class ShipModelInventoryManifest
{
    public string SchemaVersion { get; set; } = "1.3.0";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public List<string> IncludedFolders { get; set; } = new();
    public List<string> ExcludedFolders { get; set; } = new();
    public int ObjFilesScanned { get; set; }
    public int UsedPrimary { get; set; }
    public int UsedMultipart { get; set; }
    public int UsedConfigured { get; set; }
    public int UsedPipelineInput { get; set; }
    public int ReviewCandidates { get; set; }
    public int DuplicateHashGroups { get; set; }
    public List<string> MissingConfiguredModels { get; set; } = new();
    public List<string> MultipartErrors { get; set; } = new();
    public List<ShipModelInventoryEntry> Entries { get; set; } = new();
}

public sealed class ShipModelInventoryEntry
{
    public string RepositoryPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string CurrentFolderClass { get; set; } = string.Empty;
    public List<string> FirstEditionBaseSizes { get; set; } = new();
    public string RecommendedUnified1eFolder { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string UsageStatus { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> UsageTypes { get; set; } = new();
    public List<string> UsageSources { get; set; } = new();
    public List<string> ShipGroups { get; set; } = new();
    public bool IsMultipartMember { get; set; }
    public string MultipartSet { get; set; } = string.Empty;
    public List<string> DuplicatePaths { get; set; } = new();
}

internal sealed class ShipModelUsage
{
    public HashSet<string> UsageTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UsageSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ShipGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FirstEditionBaseSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
}
