namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class Unified1eAssetMigrationPlan
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string DestinationRoot { get; set; } = "assets/source/unified1e";
    public int ShipFolders { get; set; }
    public int BaseFolders { get; set; }
    public int AdditionalFiles { get; set; }
    public int Ready { get; set; }
    public int ManualReviewRequired { get; set; }
    public int Conflicts { get; set; }
    public List<Unified1eAssetMigrationEntry> Entries { get; set; } = [];
}

public sealed class Unified1eAssetMigrationEntry
{
    public string Kind { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string SourceFolderName { get; set; } = string.Empty;
    public string CanonicalFirstEditionId { get; set; } = string.Empty;
    public string CurrentFolderClass { get; set; } = string.Empty;
    public string FirstEditionBaseSize { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long SizeBytes { get; set; }
    public string Status { get; set; } = "Ready";
    public List<string> Reasons { get; set; } = [];
}
