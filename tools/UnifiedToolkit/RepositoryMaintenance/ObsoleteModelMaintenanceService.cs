using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class ObsoleteModelMaintenanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RepositoryReferenceScanner scanner = new();

    public ObsoleteModelVerificationManifest Verify(
        string repositoryRoot,
        string auditPath,
        string mode)
    {
        var auditEntries = JsonSerializer.Deserialize<List<ShipModelSelectionAuditEntry>>(
            File.ReadAllText(auditPath),
            JsonOptions) ?? new List<ShipModelSelectionAuditEntry>();

        var eligible = auditEntries
            .Where(entry =>
                entry.CleanupStatus.Equals("CleanupCandidate", StringComparison.OrdinalIgnoreCase)
                || entry.CleanupStatus.Equals("Obsolete", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                entry => Normalise(entry.RejectedModelPath),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Faction, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.ShipName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RejectedModelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var manifest = new ObsoleteModelVerificationManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalise(repositoryRoot),
            AuditPath = Normalise(auditPath),
            Mode = mode,
            EntriesScanned = eligible.Count
        };

        foreach (var audit in eligible)
        {
            var originalPath = Normalise(audit.RejectedModelPath);
            var replacementPath = Normalise(audit.SelectedModelPath);
            var originalFullPath = Resolve(repositoryRoot, originalPath);
            var replacementFullPath = Resolve(repositoryRoot, replacementPath);
            var originalExists = File.Exists(originalFullPath);
            var replacementExists = File.Exists(replacementFullPath);
            var selectedByOtherShips = auditEntries
                .Where(other =>
                    Normalise(other.SelectedModelPath).Equals(
                        originalPath,
                        StringComparison.OrdinalIgnoreCase)
                    && !Normalise(other.RejectedModelPath).Equals(
                        originalPath,
                        StringComparison.OrdinalIgnoreCase))
                .Select(other => $"{other.Faction}/{other.ShipId} ({other.ShipName})")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var classifiedReferences = originalExists
                ? scanner.FindReferences(repositoryRoot, originalPath).ToList()
                : new List<RepositoryReference>();
            var blockingReferences = classifiedReferences
                .Where(reference => reference.BlocksCleanup)
                .Select(reference => reference.Path)
                .ToList();

            var status = !replacementExists
                ? "MissingReplacement"
                : selectedByOtherShips.Count > 0
                    ? "SharedSelectedAsset"
                    : !originalExists
                        ? IsQuarantined(repositoryRoot, originalPath)
                            ? "Quarantined"
                            : "MissingOriginal"
                        : blockingReferences.Count > 0
                            ? "Blocked"
                            : "VerifiedUnused";

            var entry = new ObsoleteModelVerificationEntry
            {
                Faction = audit.Faction,
                ShipId = audit.ShipId,
                ShipName = audit.ShipName,
                OriginalPath = originalPath,
                ReplacementPath = replacementPath,
                OriginalExists = originalExists,
                ReplacementExists = replacementExists,
                OriginalSizeBytes = originalExists ? new FileInfo(originalFullPath).Length : 0,
                OriginalSha256 = originalExists ? Sha256(originalFullPath) : string.Empty,
                References = classifiedReferences
                    .Select(reference => reference.Path)
                    .ToList(),
                ClassifiedReferences = classifiedReferences,
                BlockingReferences = blockingReferences,
                ManifestReferences = PathsFor(classifiedReferences, "Manifest"),
                KnowledgeBaseReferences = PathsFor(classifiedReferences, "KnowledgeBase"),
                ReportReferences = PathsFor(classifiedReferences, "Report"),
                GeneratedReferences = PathsFor(classifiedReferences, "Generated"),
                HistoricalUnified25References = PathsFor(
                    classifiedReferences,
                    "HistoricalUnified25Source"),
                SelectedByOtherShips = selectedByOtherShips,
                NonBlockingOtherReferences = classifiedReferences
                    .Where(reference => !reference.BlocksCleanup
                        && !reference.Category.Equals("Manifest", StringComparison.OrdinalIgnoreCase)
                        && !reference.Category.Equals("KnowledgeBase", StringComparison.OrdinalIgnoreCase)
                        && !reference.Category.Equals("Report", StringComparison.OrdinalIgnoreCase)
                        && !reference.Category.Equals("Generated", StringComparison.OrdinalIgnoreCase)
                        && !reference.Category.Equals(
                            "HistoricalUnified25Source",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(reference => reference.Path)
                    .ToList(),
                VerificationStatus = status,
                VerifiedUtc = DateTimeOffset.UtcNow
            };

            manifest.Entries.Add(entry);
        }

        RefreshCounts(manifest);
        return manifest;
    }

    public void Quarantine(string repositoryRoot, ObsoleteModelVerificationManifest manifest)
    {
        foreach (var entry in manifest.Entries.Where(item =>
                     item.VerificationStatus.Equals(
                         "VerifiedUnused",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var source = Resolve(repositoryRoot, entry.OriginalPath);
            var destination = ResolveQuarantine(repositoryRoot, entry.OriginalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (File.Exists(destination))
                throw new IOException($"Quarantine destination already exists: {destination}");

            File.Move(source, destination);
            entry.Action = "Quarantined";
            entry.QuarantinePath = Normalise(Path.GetRelativePath(repositoryRoot, destination));
            entry.VerificationStatus = "Quarantined";
        }

        manifest.Mode = "Quarantine";
        manifest.GeneratedUtc = DateTimeOffset.UtcNow;
        RefreshCounts(manifest);
    }

    public ObsoleteModelVerificationManifest Restore(
        string repositoryRoot,
        string auditPath)
    {
        var manifest = Verify(repositoryRoot, auditPath, "Restore");

        foreach (var entry in manifest.Entries)
        {
            var quarantine = ResolveQuarantine(repositoryRoot, entry.OriginalPath);
            if (!File.Exists(quarantine))
                continue;

            var destination = Resolve(repositoryRoot, entry.OriginalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (File.Exists(destination))
            {
                entry.Action = "RestoreBlockedDestinationExists";
                entry.VerificationStatus = "Blocked";
                entry.References.Add(Normalise(entry.OriginalPath));
                continue;
            }

            File.Move(quarantine, destination);
            entry.Action = "Restored";
            entry.VerificationStatus = "Restored";
            entry.QuarantinePath = Normalise(Path.GetRelativePath(repositoryRoot, quarantine));
        }

        manifest.GeneratedUtc = DateTimeOffset.UtcNow;
        RefreshCounts(manifest);
        return manifest;
    }

    public ObsoleteModelVerificationManifest Purge(
        string repositoryRoot,
        string auditPath)
    {
        var manifest = Verify(repositoryRoot, auditPath, "Purge");

        foreach (var entry in manifest.Entries)
        {
            var quarantine = ResolveQuarantine(repositoryRoot, entry.OriginalPath);
            if (!File.Exists(quarantine))
                continue;

            if (entry.SelectedByOtherShips.Count > 0)
            {
                entry.Action = "PurgeProtectedSharedSelectedAsset";
                entry.VerificationStatus = "SharedSelectedAsset";
                entry.QuarantinePath = Normalise(
                    Path.GetRelativePath(repositoryRoot, quarantine));
                continue;
            }

            entry.OriginalSizeBytes = new FileInfo(quarantine).Length;
            entry.OriginalSha256 = Sha256(quarantine);
            File.Delete(quarantine);
            entry.Action = "Purged";
            entry.VerificationStatus = "Purged";
            entry.QuarantinePath = Normalise(Path.GetRelativePath(repositoryRoot, quarantine));
        }

        manifest.GeneratedUtc = DateTimeOffset.UtcNow;
        RefreshCounts(manifest);
        return manifest;
    }

    public void WriteReports(
        string repositoryRoot,
        ObsoleteModelVerificationManifest manifest)
    {
        var folder = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "model-cleanup");
        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "obsolete-model-cleanup.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));

        using (var writer = new StreamWriter(
                   Path.Combine(folder, "obsolete-model-cleanup.csv"),
                   false,
                   new UTF8Encoding(false)))
        {
            writer.WriteLine(
                "Faction,ShipId,ShipName,OriginalPath,ReplacementPath," +
                "OriginalExists,ReplacementExists,OriginalSizeBytes,Sha256," +
                "VerificationStatus,Action,QuarantinePath,BlockingReferences," +
                "ManifestReferences,KnowledgeBaseReferences,ReportReferences," +
                "GeneratedReferences,HistoricalUnified25References," +
                "SelectedByOtherShips,OtherNonBlockingReferences,AllReferences");

            foreach (var entry in manifest.Entries)
            {
                writer.WriteLine(string.Join(",",
                    Csv(entry.Faction),
                    Csv(entry.ShipId),
                    Csv(entry.ShipName),
                    Csv(entry.OriginalPath),
                    Csv(entry.ReplacementPath),
                    Csv(entry.OriginalExists.ToString()),
                    Csv(entry.ReplacementExists.ToString()),
                    Csv(entry.OriginalSizeBytes.ToString()),
                    Csv(entry.OriginalSha256),
                    Csv(entry.VerificationStatus),
                    Csv(entry.Action),
                    Csv(entry.QuarantinePath),
                    Csv(string.Join(" | ", entry.BlockingReferences)),
                    Csv(string.Join(" | ", entry.ManifestReferences)),
                    Csv(string.Join(" | ", entry.KnowledgeBaseReferences)),
                    Csv(string.Join(" | ", entry.ReportReferences)),
                    Csv(string.Join(" | ", entry.GeneratedReferences)),
                    Csv(string.Join(" | ", entry.HistoricalUnified25References)),
                    Csv(string.Join(" | ", entry.SelectedByOtherShips)),
                    Csv(string.Join(" | ", entry.NonBlockingOtherReferences)),
                    Csv(string.Join(" | ", entry.References))));
            }
        }

        using var markdown = new StreamWriter(
            Path.Combine(folder, "OBSOLETE-MODEL-CLEANUP.md"),
            false,
            new UTF8Encoding(false));
        markdown.WriteLine("# Obsolete Model Cleanup");
        markdown.WriteLine();
        markdown.WriteLine($"Mode: **{manifest.Mode}**");
        markdown.WriteLine();
        markdown.WriteLine($"- Entries scanned: {manifest.EntriesScanned}");
        markdown.WriteLine($"- Verified unused: {manifest.VerifiedUnused}");
        markdown.WriteLine($"- Blocked: {manifest.Blocked}");
        markdown.WriteLine($"- Shared selected assets: {manifest.SharedSelectedAsset}");
        markdown.WriteLine($"- Missing original: {manifest.MissingOriginal}");
        markdown.WriteLine($"- Missing replacement: {manifest.MissingReplacement}");
        markdown.WriteLine($"- Quarantined: {manifest.Quarantined}");
        markdown.WriteLine($"- Restored: {manifest.Restored}");
        markdown.WriteLine($"- Purged: {manifest.Purged}");
        markdown.WriteLine();
        markdown.WriteLine("Only active runtime, active source-asset or unknown/other references block cleanup.");
        markdown.WriteLine("Retained Unified 2.5 source snapshots, manifests, knowledge-base records, reports and generated references remain visible but do not block quarantine.");
        markdown.WriteLine("Any rejected OBJ that is also the confirmed selected OBJ for another ship is protected as SharedSelectedAsset.");
        markdown.WriteLine();
        markdown.WriteLine("| Ship | Original OBJ | Replacement OBJ | Status | Selected by | Blocking | Historical 2.5 | Manifest | KB | Reports | Generated |");
        markdown.WriteLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var entry in manifest.Entries)
        {
            markdown.WriteLine(
                $"| {Escape(entry.ShipName)} | `{entry.OriginalPath}` | " +
                $"`{entry.ReplacementPath}` | {entry.VerificationStatus} | " +
                $"{Escape(string.Join("; ", entry.SelectedByOtherShips))} | " +
                $"{entry.BlockingReferences.Count} | " +
                $"{entry.HistoricalUnified25References.Count} | " +
                $"{entry.ManifestReferences.Count} | " +
                $"{entry.KnowledgeBaseReferences.Count} | {entry.ReportReferences.Count} | " +
                $"{entry.GeneratedReferences.Count} |");
        }
    }


    private static List<string> PathsFor(
        IEnumerable<RepositoryReference> references,
        string category) =>
        references
            .Where(reference => reference.Category.Equals(
                category,
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.Path)
            .ToList();

    private static void RefreshCounts(ObsoleteModelVerificationManifest manifest)
    {
        manifest.VerifiedUnused = Count(manifest, "VerifiedUnused");
        manifest.Blocked = Count(manifest, "Blocked");
        manifest.SharedSelectedAsset = Count(manifest, "SharedSelectedAsset");
        manifest.MissingOriginal = Count(manifest, "MissingOriginal");
        manifest.MissingReplacement = Count(manifest, "MissingReplacement");
        manifest.Quarantined = Count(manifest, "Quarantined");
        manifest.Restored = Count(manifest, "Restored");
        manifest.Purged = Count(manifest, "Purged");
    }

    private static int Count(ObsoleteModelVerificationManifest manifest, string status) =>
        manifest.Entries.Count(entry =>
            entry.VerificationStatus.Equals(status, StringComparison.OrdinalIgnoreCase));

    private static bool IsQuarantined(string repositoryRoot, string relativePath) =>
        File.Exists(ResolveQuarantine(repositoryRoot, relativePath));

    private static string Resolve(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ResolveQuarantine(string root, string relativePath) =>
        Path.Combine(
            root,
            "_unifiedtoolkit_quarantine",
            "obsolete-models",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Sha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Csv(string value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private static string Escape(string value) =>
        (value ?? string.Empty).Replace("|", "\\|");

    private static string Normalise(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
