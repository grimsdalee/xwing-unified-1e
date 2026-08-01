namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class ShipModelSelectionAuditEntry
{
    public string Faction { get; set; } = string.Empty;
    public string ShipId { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public string RejectedModelPath { get; set; } = string.Empty;
    public string SelectedModelPath { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string CleanupStatus { get; set; } = string.Empty;
    public DateTimeOffset LastConfirmedUtc { get; set; }
}

public sealed class RepositoryReference
{
    public string Path { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool BlocksCleanup { get; set; }
}

public sealed class ObsoleteModelVerificationEntry
{
    public string Faction { get; set; } = string.Empty;
    public string ShipId { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string ReplacementPath { get; set; } = string.Empty;
    public bool OriginalExists { get; set; }
    public bool ReplacementExists { get; set; }
    public long OriginalSizeBytes { get; set; }
    public string OriginalSha256 { get; set; } = string.Empty;

    // Retained for compatibility with the first report schema.
    public List<string> References { get; set; } = new();

    public List<RepositoryReference> ClassifiedReferences { get; set; } = new();
    public List<string> BlockingReferences { get; set; } = new();
    public List<string> ManifestReferences { get; set; } = new();
    public List<string> KnowledgeBaseReferences { get; set; } = new();
    public List<string> ReportReferences { get; set; } = new();
    public List<string> GeneratedReferences { get; set; } = new();
    public List<string> HistoricalUnified25References { get; set; } = new();
    public List<string> NonBlockingOtherReferences { get; set; } = new();

    // A rejected model may still be the confirmed selected model for another
    // ship. Such shared assets are protected from quarantine and purge.
    public List<string> SelectedByOtherShips { get; set; } = new();

    public string VerificationStatus { get; set; } = string.Empty;
    public string Action { get; set; } = "None";
    public string QuarantinePath { get; set; } = string.Empty;
    public DateTimeOffset VerifiedUtc { get; set; }
}

public sealed class ObsoleteModelVerificationManifest
{
    public string SchemaVersion { get; set; } = "1.2.0";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string AuditPath { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int EntriesScanned { get; set; }
    public int VerifiedUnused { get; set; }
    public int Blocked { get; set; }
    public int SharedSelectedAsset { get; set; }
    public int MissingOriginal { get; set; }
    public int MissingReplacement { get; set; }
    public int Quarantined { get; set; }
    public int Restored { get; set; }
    public int Purged { get; set; }
    public List<ObsoleteModelVerificationEntry> Entries { get; set; } = new();
}
