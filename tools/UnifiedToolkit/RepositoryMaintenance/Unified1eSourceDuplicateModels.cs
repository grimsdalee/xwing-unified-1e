namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class Unified1eSourceDuplicateAudit
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public List<string> SourceRoots { get; set; } = new();
    public string AuthoritativeRoot { get; set; } = string.Empty;
    public int FilesScanned { get; set; }
    public int ExactDuplicates { get; set; }
    public int ReadyToQuarantine { get; set; }
    public int BlockedByReferences { get; set; }
    public int NoUnified1eDuplicate { get; set; }
    public int AlreadyMissing { get; set; }
    public long ReadyBytes { get; set; }
    public List<Unified1eSourceDuplicateEntry> Entries { get; set; } = new();
}

public sealed class Unified1eSourceDuplicateEntry
{
    public string SourcePath { get; set; } = string.Empty;
    public string AuthoritativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> BlockingReferences { get; set; } = new();
    public List<string> NonBlockingReferences { get; set; } = new();
    public string QuarantinePath { get; set; } = string.Empty;
}

public static class Unified1eSourceDuplicateStatuses
{
    public const string ReadyToQuarantine = "ReadyToQuarantine";
    public const string BlockedByReferences = "BlockedByReferences";
    public const string NoUnified1eDuplicate = "NoUnified1eDuplicate";
    public const string AlreadyMissing = "AlreadyMissing";
    public const string Quarantined = "Quarantined";
}
