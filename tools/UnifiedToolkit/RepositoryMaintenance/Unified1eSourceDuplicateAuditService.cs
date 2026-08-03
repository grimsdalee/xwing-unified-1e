using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.RepositoryMaintenance;

public static class Unified1eSourceDuplicateAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> TextExtensions = new(
        new[]
        {
            ".cs", ".json", ".lua", ".xml", ".txt", ".md", ".csv",
            ".yml", ".yaml", ".ps1", ".props", ".targets"
        },
        StringComparer.OrdinalIgnoreCase);

    public static Unified1eSourceDuplicateAudit Audit(
        string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);

        var authoritativeRoot = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e");
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "assets", "source", "unified25"),
            Path.Combine(repositoryRoot, "assets", "source", "xwing-data")
        };

        if (!Directory.Exists(authoritativeRoot))
        {
            throw new DirectoryNotFoundException(
                $"Authoritative Unified 1E asset root was not found: {authoritativeRoot}");
        }

        var unified1eByHash = BuildHashIndex(authoritativeRoot);
        var referenceFiles = LoadReferenceFiles(repositoryRoot);
        var audit = new Unified1eSourceDuplicateAudit
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalise(Path.GetRelativePath(repositoryRoot, repositoryRoot)),
            AuthoritativeRoot = Normalise(
                Path.GetRelativePath(repositoryRoot, authoritativeRoot)),
            SourceRoots = sourceRoots
                .Select(path => Normalise(Path.GetRelativePath(repositoryRoot, path)))
                .ToList()
        };

        foreach (var sourceRoot in sourceRoots)
        {
            if (!Directory.Exists(sourceRoot))
                continue;

            foreach (var file in Directory.EnumerateFiles(
                         sourceRoot,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                audit.FilesScanned++;
                var sourcePath = Normalise(
                    Path.GetRelativePath(repositoryRoot, file));
                var info = new FileInfo(file);
                var hash = HashFile(file);

                if (!unified1eByHash.TryGetValue(hash, out var matches)
                    || matches.Count == 0)
                {
                    audit.NoUnified1eDuplicate++;
                    audit.Entries.Add(new Unified1eSourceDuplicateEntry
                    {
                        SourcePath = sourcePath,
                        Sha256 = hash,
                        SizeBytes = info.Length,
                        Status = Unified1eSourceDuplicateStatuses.NoUnified1eDuplicate,
                        RecommendedAction = "Retain"
                    });
                    continue;
                }

                audit.ExactDuplicates++;
                var authoritativePath = matches
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .First();
                var references = FindReferences(
                    sourcePath,
                    repositoryRoot,
                    referenceFiles);

                var entry = new Unified1eSourceDuplicateEntry
                {
                    SourcePath = sourcePath,
                    AuthoritativePath = authoritativePath,
                    Sha256 = hash,
                    SizeBytes = info.Length,
                    BlockingReferences = references.Blocking,
                    NonBlockingReferences = references.NonBlocking
                };

                if (entry.BlockingReferences.Count > 0)
                {
                    entry.Status = Unified1eSourceDuplicateStatuses.BlockedByReferences;
                    entry.RecommendedAction = "RewireOrRetain";
                    audit.BlockedByReferences++;
                }
                else
                {
                    entry.Status = Unified1eSourceDuplicateStatuses.ReadyToQuarantine;
                    entry.RecommendedAction = "Quarantine";
                    audit.ReadyToQuarantine++;
                    audit.ReadyBytes += info.Length;
                }

                audit.Entries.Add(entry);
            }
        }

        return audit;
    }

    public static Unified1eSourceDuplicateAudit Quarantine(
        string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var audit = Audit(repositoryRoot);
        var batch = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var quarantineRoot = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_quarantine",
            "source-duplicates",
            batch);

        foreach (var entry in audit.Entries.Where(entry =>
                     entry.Status == Unified1eSourceDuplicateStatuses.ReadyToQuarantine))
        {
            var source = Path.Combine(
                repositoryRoot,
                entry.SourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                entry.Status = Unified1eSourceDuplicateStatuses.AlreadyMissing;
                entry.RecommendedAction = "Review";
                audit.AlreadyMissing++;
                continue;
            }

            var destination = Path.Combine(
                quarantineRoot,
                entry.SourcePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: false);
            entry.QuarantinePath = Normalise(
                Path.GetRelativePath(repositoryRoot, destination));
            entry.Status = Unified1eSourceDuplicateStatuses.Quarantined;
            entry.RecommendedAction = "ValidateThenPurge";
        }

        RemoveEmptyDirectories(
            Path.Combine(repositoryRoot, "assets", "source", "unified25"));
        RemoveEmptyDirectories(
            Path.Combine(repositoryRoot, "assets", "source", "xwing-data"));

        return audit;
    }

    public static void WriteReports(
        string repositoryRoot,
        Unified1eSourceDuplicateAudit audit)
    {
        var output = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase14",
            "source-duplicate-cleanup");
        Directory.CreateDirectory(output);

        File.WriteAllText(
            Path.Combine(output, "source-duplicate-audit.json"),
            JsonSerializer.Serialize(audit, JsonOptions),
            new UTF8Encoding(false));

        using (var writer = new StreamWriter(
                   Path.Combine(output, "source-duplicate-audit.csv"),
                   false,
                   new UTF8Encoding(false)))
        {
            writer.WriteLine(
                "SourcePath,AuthoritativePath,Sha256,SizeBytes,Status," +
                "RecommendedAction,BlockingReferences,NonBlockingReferences," +
                "QuarantinePath");
            foreach (var entry in audit.Entries)
            {
                writer.WriteLine(string.Join(",",
                    Csv(entry.SourcePath),
                    Csv(entry.AuthoritativePath),
                    Csv(entry.Sha256),
                    entry.SizeBytes,
                    Csv(entry.Status),
                    Csv(entry.RecommendedAction),
                    Csv(string.Join(" | ", entry.BlockingReferences)),
                    Csv(string.Join(" | ", entry.NonBlockingReferences)),
                    Csv(entry.QuarantinePath)));
            }
        }

        using var markdown = new StreamWriter(
            Path.Combine(output, "SOURCE-DUPLICATE-AUDIT.md"),
            false,
            new UTF8Encoding(false));
        markdown.WriteLine("# Unified 1E Source Duplicate Audit");
        markdown.WriteLine();
        markdown.WriteLine($"- Files scanned: {audit.FilesScanned}");
        markdown.WriteLine($"- Exact duplicates: {audit.ExactDuplicates}");
        markdown.WriteLine($"- Ready to quarantine: {audit.ReadyToQuarantine}");
        markdown.WriteLine($"- Blocked by references: {audit.BlockedByReferences}");
        markdown.WriteLine($"- No Unified 1E duplicate: {audit.NoUnified1eDuplicate}");
        markdown.WriteLine($"- Ready bytes: {audit.ReadyBytes}");
        markdown.WriteLine();
        markdown.WriteLine(
            "Only byte-identical files with a surviving copy under " +
            "`assets/source/unified1e` and no active blocking references are " +
            "eligible for quarantine.");
    }

    private static Dictionary<string, List<string>> BuildHashIndex(
        string authoritativeRoot)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            authoritativeRoot,
            "..",
            "..",
            ".."));
        var result = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(
                     authoritativeRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var hash = HashFile(file);
            if (!result.TryGetValue(hash, out var paths))
            {
                paths = new List<string>();
                result[hash] = paths;
            }
            paths.Add(Normalise(Path.GetRelativePath(repositoryRoot, file)));
        }

        return result;
    }

    private static List<ReferenceFile> LoadReferenceFiles(
        string repositoryRoot)
    {
        var results = new List<ReferenceFile>();
        foreach (var file in Directory.EnumerateFiles(
                     repositoryRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Normalise(Path.GetRelativePath(repositoryRoot, file));
            if (!TextExtensions.Contains(Path.GetExtension(file)))
                continue;
            if (IsExcludedReferencePath(relative))
                continue;

            try
            {
                var text = File.ReadAllText(file);
                results.Add(new ReferenceFile(
                    relative,
                    text,
                    IsNonBlockingReferencePath(relative)));
            }
            catch (IOException)
            {
                // A transiently locked text file is omitted from this audit.
            }
            catch (UnauthorizedAccessException)
            {
                // An inaccessible text file is omitted from this audit.
            }
        }
        return results;
    }

    private static ReferenceMatches FindReferences(
        string sourcePath,
        string repositoryRoot,
        IReadOnlyList<ReferenceFile> files)
    {
        var blocking = new List<string>();
        var nonBlocking = new List<string>();
        var variants = BuildReferenceVariants(sourcePath);

        foreach (var file in files)
        {
            if (!variants.Any(variant => file.Text.Contains(
                    variant,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (file.NonBlocking)
                nonBlocking.Add(file.RelativePath);
            else
                blocking.Add(file.RelativePath);
        }

        return new ReferenceMatches(
            blocking.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            nonBlocking.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyList<string> BuildReferenceVariants(
        string sourcePath)
    {
        var slash = sourcePath.Replace('\\', '/');
        var backslash = slash.Replace('/', '\\');
        return new[]
        {
            slash,
            backslash,
            "https://raw.githubusercontent.com/grimsdalee/" +
            "xwing-unified-1e/main/" + slash
        };
    }

    private static bool IsExcludedReferencePath(string path) =>
        path.StartsWith("assets/source/unified25/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("assets/source/xwing-data/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("assets/source/unified1e/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("_unifiedtoolkit_reports/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("_unifiedtoolkit_backups/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("_unifiedtoolkit_quarantine/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("source/unified-2.5/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("source/legacy-1e/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonBlockingReferencePath(string path) =>
        // Derived indexes and generated outputs are rebuilt after source
        // quarantine. They provide useful provenance evidence, but must not
        // prevent exact duplicate source files from being quarantined.
        path.StartsWith(
            "ukb/",
            StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(
            "assets/manifests/",
            StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(
            "assets/generated/",
            StringComparison.OrdinalIgnoreCase)

        // Import manifests preserve original source provenance. They should
        // remain readable after consolidation, but references recorded inside
        // them are historical evidence rather than active asset dependencies.
        || path.Equals(
            "source/xwing-data/import-manifest.json",
            StringComparison.OrdinalIgnoreCase)

        // Migration, audit and selection source code intentionally retains
        // old paths as historical inputs or mapping keys.
        || path.EndsWith(
            "Commands/GeneratePrototypeSaveCommand.cs",
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            "/RepositoryMaintenance/",
            StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(
            "ConversionData/first-edition/ship-folder-map.json",
            StringComparison.OrdinalIgnoreCase);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var directory in Directory.EnumerateDirectories(
                     root,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    private static string Normalise(string path) =>
        path.Replace('\\', '/');

    private static string Csv(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';

    private sealed record ReferenceFile(
        string RelativePath,
        string Text,
        bool NonBlocking);

    private sealed record ReferenceMatches(
        List<string> Blocking,
        List<string> NonBlocking);
}
