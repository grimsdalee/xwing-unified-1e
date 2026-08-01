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
            var references = originalExists
                ? scanner.FindReferences(repositoryRoot, originalPath).ToList()
                : new List<string>();

            var status = !replacementExists
                ? "MissingReplacement"
                : !originalExists
                    ? IsQuarantined(repositoryRoot, originalPath)
                        ? "Quarantined"
                        : "MissingOriginal"
                    : references.Count > 0
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
                References = references,
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
                "VerificationStatus,Action,QuarantinePath,References");

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
        markdown.WriteLine($"- Missing original: {manifest.MissingOriginal}");
        markdown.WriteLine($"- Missing replacement: {manifest.MissingReplacement}");
        markdown.WriteLine($"- Quarantined: {manifest.Quarantined}");
        markdown.WriteLine($"- Restored: {manifest.Restored}");
        markdown.WriteLine($"- Purged: {manifest.Purged}");
        markdown.WriteLine();
        markdown.WriteLine("| Ship | Original OBJ | Replacement OBJ | Status | Action | References |");
        markdown.WriteLine("|---|---|---|---|---|---|");
        foreach (var entry in manifest.Entries)
        {
            markdown.WriteLine(
                $"| {Escape(entry.ShipName)} | `{entry.OriginalPath}` | " +
                $"`{entry.ReplacementPath}` | {entry.VerificationStatus} | " +
                $"{entry.Action} | {entry.References.Count} |");
        }
    }

    private static void RefreshCounts(ObsoleteModelVerificationManifest manifest)
    {
        manifest.VerifiedUnused = Count(manifest, "VerifiedUnused");
        manifest.Blocked = Count(manifest, "Blocked");
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
