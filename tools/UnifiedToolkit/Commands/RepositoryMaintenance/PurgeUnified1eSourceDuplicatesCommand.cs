using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class PurgeUnified1eSourceDuplicatesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        var repositoryRoot = Path.GetFullPath(args[0]);
        var confirmed = args.Any(argument =>
            argument.Equals(
                "--confirm-purge",
                StringComparison.OrdinalIgnoreCase));

        if (!confirmed)
        {
            Console.Error.WriteLine(
                "Permanent purge requires --confirm-purge.");
            ShowUsage();
            return 1;
        }

        try
        {
            var quarantineRoot = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_quarantine",
                "source-duplicates");

            if (!Directory.Exists(quarantineRoot))
            {
                Console.WriteLine(
                    "No source-duplicate quarantine folder exists.");
                return 0;
            }

            var reportRoot = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase14",
                "source-duplicate-cleanup");
            Directory.CreateDirectory(reportRoot);

            var auditPath = Path.Combine(
                reportRoot,
                "source-duplicate-audit.json");
            if (!File.Exists(auditPath))
            {
                throw new FileNotFoundException(
                    "The source duplicate audit is required before purge.",
                    auditPath);
            }

            var ledgerPath = Path.Combine(
                reportRoot,
                "source-duplicate-purge-ledger.json");
            var csvPath = Path.Combine(
                reportRoot,
                "source-duplicate-purge-ledger.csv");

            var approved = LoadApprovedEntries(auditPath);
            var ledger = LoadLedger(ledgerPath);
            var purgedCount = 0;
            long bytesPurged = 0;
            var skippedCount = 0;
            var errors = new List<string>();

            foreach (var batchDirectory in Directory
                         .EnumerateDirectories(
                             quarantineRoot,
                             "*",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(
                             path => path,
                             StringComparer.OrdinalIgnoreCase))
            {
                var batchId = Path.GetFileName(batchDirectory);
                var batchEntries =
                    new List<Unified1eSourceDuplicatePurgeEntry>();
                long batchBytes = 0;

                foreach (var quarantineFile in Directory
                             .EnumerateFiles(
                                 batchDirectory,
                                 "*",
                                 SearchOption.AllDirectories)
                             .OrderBy(
                                 path => path,
                                 StringComparer.OrdinalIgnoreCase)
                             .ToList())
                {
                    try
                    {
                        var originalPath = Normalise(
                            Path.GetRelativePath(
                                batchDirectory,
                                quarantineFile));

                        if (!approved.TryGetValue(
                                originalPath,
                                out var approvedEntry))
                        {
                            skippedCount++;
                            errors.Add(
                                $"No approved audit entry exists for '{originalPath}'.");
                            continue;
                        }

                        var quarantineInfo = new FileInfo(quarantineFile);
                        var quarantineLength = quarantineInfo.Length;
                        var quarantineHash = HashFile(quarantineFile);

                        if (!quarantineHash.Equals(
                                approvedEntry.Sha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            skippedCount++;
                            errors.Add(
                                $"Quarantined hash changed for '{originalPath}'.");
                            continue;
                        }

                        var unified1eFullPath = Path.Combine(
                            repositoryRoot,
                            approvedEntry.Unified1ePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar));

                        if (!File.Exists(unified1eFullPath))
                        {
                            skippedCount++;
                            errors.Add(
                                $"Authoritative file is missing: " +
                                $"'{approvedEntry.Unified1ePath}'.");
                            continue;
                        }

                        if (!HashFile(unified1eFullPath).Equals(
                                quarantineHash,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            skippedCount++;
                            errors.Add(
                                $"Authoritative hash mismatch for " +
                                $"'{approvedEntry.Unified1ePath}'.");
                            continue;
                        }

                        File.Delete(quarantineFile);

                        if (!ledger.Entries.Any(entry =>
                                entry.OriginalPath.Equals(
                                    originalPath,
                                    StringComparison.OrdinalIgnoreCase)
                                && entry.Sha256.Equals(
                                    quarantineHash,
                                    StringComparison.OrdinalIgnoreCase)))
                        {
                            var entry =
                                new Unified1eSourceDuplicatePurgeEntry
                                {
                                    BatchId = batchId,
                                    PurgedUtc = DateTimeOffset.UtcNow,
                                    OriginalPath = originalPath,
                                    QuarantinePath = Normalise(
                                        Path.GetRelativePath(
                                            repositoryRoot,
                                            quarantineFile)),
                                    Unified1ePath =
                                        approvedEntry.Unified1ePath,
                                    Sha256 = quarantineHash,
                                    SizeBytes = quarantineLength,
                                    Status = "Purged"
                                };

                            ledger.Entries.Add(entry);
                            batchEntries.Add(entry);
                        }

                        purgedCount++;
                        bytesPurged += quarantineLength;
                        batchBytes += quarantineLength;
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        errors.Add(
                            $"{Normalise(quarantineFile)}: {ex.Message}");
                    }
                }

                if (batchEntries.Count > 0)
                {
                    ledger.Batches.Add(
                        new Unified1eSourceDuplicatePurgeBatch
                        {
                            BatchId = batchId,
                            PurgedUtc = DateTimeOffset.UtcNow,
                            QuarantineRoot = Normalise(
                                Path.GetRelativePath(
                                    repositoryRoot,
                                    batchDirectory)),
                            EntryCount = batchEntries.Count,
                            BytesPurged = batchBytes,
                            ValidationNote =
                                "Purged only after SHA-256 matched the " +
                                "authoritative Unified1e copy."
                        });
                }

                DeleteEmptyDirectories(batchDirectory);
            }

            var recovered = RecoverAlreadyDeletedEntries(
                repositoryRoot,
                auditPath,
                ledger,
                errors);

            purgedCount += recovered.Count;
            bytesPurged += recovered.Sum(entry => entry.SizeBytes);

            foreach (var group in recovered.GroupBy(
                         entry => entry.BatchId,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (ledger.Batches.Any(batch =>
                        batch.BatchId.Equals(
                            group.Key,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                ledger.Batches.Add(
                    new Unified1eSourceDuplicatePurgeBatch
                    {
                        BatchId = group.Key,
                        PurgedUtc = DateTimeOffset.UtcNow,
                        QuarantineRoot =
                            $"_unifiedtoolkit_quarantine/source-duplicates/{group.Key}",
                        EntryCount = group.Count(),
                        BytesPurged = group.Sum(entry => entry.SizeBytes),
                        ValidationNote =
                            "Recovered purge history after files were deleted " +
                            "before the ledger write completed. The original " +
                            "source is absent and the authoritative Unified1e " +
                            "copy still matches the recorded SHA-256."
                    });
            }

            ledger.UpdatedUtc = DateTimeOffset.UtcNow;
            ledger.Entries = ledger.Entries
                .OrderBy(
                    entry => entry.OriginalPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            ledger.Batches = ledger.Batches
                .GroupBy(
                    batch => batch.BatchId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(batch => batch.PurgedUtc)
                .ToList();

            File.WriteAllText(
                ledgerPath,
                JsonSerializer.Serialize(ledger, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, ledger);

            Console.WriteLine(
                "UnifiedToolkit Unified 1E Source Duplicate Purge");
            Console.WriteLine(
                "================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine();
            Console.WriteLine($"Purged:                 {purgedCount}");
            Console.WriteLine($"Purged bytes:           {bytesPurged}");
            Console.WriteLine($"Skipped:                {skippedCount}");
            Console.WriteLine($"Errors:                 {errors.Count}");
            Console.WriteLine($"Ledger:                 {ledgerPath}");
            Console.WriteLine($"CSV:                    {csvPath}");
            Console.WriteLine();

            if (errors.Count > 0)
            {
                Console.WriteLine("Errors:");
                foreach (var error in errors.Take(20))
                    Console.WriteLine($"  - {error}");

                if (errors.Count > 20)
                {
                    Console.WriteLine(
                        $"  ... and {errors.Count - 20} more.");
                }
            }

            Console.WriteLine(
                "Only quarantined files with a matching authoritative " +
                "Unified1e SHA-256 were permanently deleted.");

            return errors.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Source duplicate purge failed: {ex.Message}");
            return 1;
        }
    }

    private static List<Unified1eSourceDuplicatePurgeEntry>
        RecoverAlreadyDeletedEntries(
            string repositoryRoot,
            string auditPath,
            Unified1eSourceDuplicatePurgeLedger ledger,
            ICollection<string> errors)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(auditPath));

        if (!document.RootElement.TryGetProperty(
                "entries",
                out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var recovered =
            new List<Unified1eSourceDuplicatePurgeEntry>();

        foreach (var auditEntry in entries.EnumerateArray())
        {
            var status = ReadString(auditEntry, "status");
            if (!status.Equals(
                    "Quarantined",
                    StringComparison.OrdinalIgnoreCase)
                && !status.Equals(
                    "ReadyToQuarantine",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var originalPath = ReadString(
                auditEntry,
                "sourcePath");
            var authoritativePath = ReadString(
                auditEntry,
                "authoritativePath");
            var quarantinePath = ReadString(
                auditEntry,
                "quarantinePath");
            var sha256 = ReadString(
                auditEntry,
                "sha256");
            var sizeBytes = auditEntry.TryGetProperty(
                    "sizeBytes",
                    out var sizeElement)
                && sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;

            if (originalPath.Length == 0
                || authoritativePath.Length == 0
                || quarantinePath.Length == 0
                || sha256.Length == 0)
            {
                continue;
            }

            if (ledger.Entries.Any(entry =>
                    entry.OriginalPath.Equals(
                        originalPath,
                        StringComparison.OrdinalIgnoreCase)
                    && entry.Sha256.Equals(
                        sha256,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var originalFullPath = Path.Combine(
                repositoryRoot,
                originalPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            var quarantineFullPath = Path.Combine(
                repositoryRoot,
                quarantinePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            var authoritativeFullPath = Path.Combine(
                repositoryRoot,
                authoritativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            // Normal processing still owns files that remain in quarantine.
            if (File.Exists(quarantineFullPath))
                continue;

            // Recovery is only safe when the old source copy is also absent.
            if (File.Exists(originalFullPath))
                continue;

            if (!File.Exists(authoritativeFullPath))
            {
                errors.Add(
                    $"Cannot recover purge history because the authoritative " +
                    $"file is missing: '{authoritativePath}'.");
                continue;
            }

            if (!HashFile(authoritativeFullPath).Equals(
                    sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Cannot recover purge history because the authoritative " +
                    $"hash changed: '{authoritativePath}'.");
                continue;
            }

            var batchId = ExtractBatchId(quarantinePath);

            var entry = new Unified1eSourceDuplicatePurgeEntry
            {
                BatchId = batchId,
                PurgedUtc = DateTimeOffset.UtcNow,
                OriginalPath = originalPath,
                QuarantinePath = quarantinePath,
                Unified1ePath = authoritativePath,
                Sha256 = sha256,
                SizeBytes = sizeBytes,
                Status = "PurgedRecovered"
            };

            ledger.Entries.Add(entry);
            recovered.Add(entry);
        }

        return recovered;
    }

    private static string ExtractBatchId(
        string quarantinePath)
    {
        const string prefix =
            "_unifiedtoolkit_quarantine/source-duplicates/";

        if (!quarantinePath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return "recovered";
        }

        var remainder = quarantinePath[prefix.Length..];
        var separator = remainder.IndexOf('/');

        return separator > 0
            ? remainder[..separator]
            : remainder;
    }

    private static Dictionary<string, ApprovedEntry> LoadApprovedEntries(
        string auditPath)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(auditPath));

        if (!document.RootElement.TryGetProperty(
                "entries",
                out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The source duplicate audit has no entries array.");
        }

        var results = new Dictionary<string, ApprovedEntry>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.EnumerateArray())
        {
            var status = ReadString(entry, "status");
            if (!status.Equals(
                    "ReadyToQuarantine",
                    StringComparison.OrdinalIgnoreCase)
                && !status.Equals(
                    "Quarantined",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourcePath = ReadString(entry, "sourcePath");
            var unified1ePath = ReadString(
                entry,
                "authoritativePath");

            if (unified1ePath.Length == 0)
            {
                // Backward compatibility with the initial Phase 14 schema.
                unified1ePath = ReadString(
                    entry,
                    "unified1ePath");
            }

            var sha256 = ReadString(entry, "sha256");

            if (sourcePath.Length == 0
                || unified1ePath.Length == 0
                || sha256.Length == 0)
            {
                continue;
            }

            results[sourcePath] =
                new ApprovedEntry(unified1ePath, sha256);
        }

        return results;
    }

    private static Unified1eSourceDuplicatePurgeLedger LoadLedger(
        string ledgerPath)
    {
        if (!File.Exists(ledgerPath))
            return new Unified1eSourceDuplicatePurgeLedger();

        return JsonSerializer.Deserialize<
                Unified1eSourceDuplicatePurgeLedger>(
                File.ReadAllText(ledgerPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? new Unified1eSourceDuplicatePurgeLedger();
    }

    private static string ReadString(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(
            propertyName,
            out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private static void WriteCsv(
        string path,
        Unified1eSourceDuplicatePurgeLedger ledger)
    {
        var lines = new List<string>
        {
            "BatchId,PurgedUtc,OriginalPath,QuarantinePath," +
            "Unified1ePath,Sha256,SizeBytes,Status"
        };

        foreach (var entry in ledger.Entries)
        {
            lines.Add(string.Join(
                ",",
                Csv(entry.BatchId),
                Csv(entry.PurgedUtc.ToString("O")),
                Csv(entry.OriginalPath),
                Csv(entry.QuarantinePath),
                Csv(entry.Unified1ePath),
                Csv(entry.Sha256),
                entry.SizeBytes,
                Csv(entry.Status)));
        }

        File.WriteAllLines(
            path,
            lines,
            new UTF8Encoding(false));
    }

    private static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void DeleteEmptyDirectories(string root)
    {
        foreach (var directory in Directory
                     .EnumerateDirectories(
                         root,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            TryDeleteEmptyDirectory(directory);
        }

        TryDeleteEmptyDirectory(root);
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

    private static string Normalise(string path) =>
        path.Replace('\\', '/');

    private static void ShowUsage()
    {
        Console.WriteLine(
            "Usage: UnifiedToolkit purge-unified1e-source-duplicates " +
            "<first-edition-repo-folder> --confirm-purge");
    }

    private sealed record ApprovedEntry(
        string Unified1ePath,
        string Sha256);
}
