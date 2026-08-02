namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class PrototypeAssetDependencyAudit
{
    public string SchemaVersion { get; set; } = "3.0.0";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string ReferenceSave { get; set; } = string.Empty;
    public string ScanMode { get; set; } = "EffectiveStructuredDependencies";
    public int FilesScanned { get; set; }
    public int ReferencesFound { get; set; }
    public int UniqueDependencies { get; set; }
    public int AlreadyMigrated { get; set; }
    public int Unified25Dependencies { get; set; }
    public int RepositoryDependencies { get; set; }
    public int UpstreamDependencies { get; set; }
    public int ExternalDependencies { get; set; }
    public int EnvironmentDependencies { get; set; }
    public int RuntimeDependencies { get; set; }
    public int ShipDependencies { get; set; }
    public int SupportingDependencies { get; set; }
    public int MissingRepositoryFiles { get; set; }
    public List<PrototypeAssetDependencyEntry> Entries { get; set; } = [];
    public List<string> ScanWarnings { get; set; } = [];
}

public sealed class PrototypeAssetDependencyEntry
{
    public string Reference { get; set; } = string.Empty;
    public string NormalizedReference { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string AssetKind { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = string.Empty;
    public bool RepositoryFileExists { get; set; }
    public string MigrationEquivalentPath { get; set; } = string.Empty;
    public bool MigrationEquivalentExists { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public string SuggestedDestination { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = [];
    public List<string> JsonProperties { get; set; } = [];
    public int Occurrences { get; set; }
}
