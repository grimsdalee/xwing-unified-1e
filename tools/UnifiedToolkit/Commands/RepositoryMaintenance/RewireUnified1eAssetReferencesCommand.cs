using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

/// <summary>
/// Phase 13C: rewires active repository references from imported source asset
/// locations to their approved assets/source/unified1e destinations.
///
/// The Phase 13 migration plan is the only mapping authority. The command is
/// report-only unless --apply is supplied. Apply mode creates timestamped
/// backups and validates every modified JSON file before replacing it.
/// </summary>
public static class RewireUnified1eAssetReferencesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> TextExtensions = new(
        new[] { ".cs", ".json", ".lua", ".xml" },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedToolFiles = new(
        new[]
        {
            "Commands/RepositoryMaintenance/CopyUnified1eAssetsCommand.cs",
            "Commands/RepositoryMaintenance/PlanUnified1eAssetMigrationCommand.cs",
            "Commands/RepositoryMaintenance/AuditPrototypeAssetDependenciesCommand.cs",
            "Commands/RepositoryMaintenance/RewireUnified1eAssetReferencesCommand.cs",
            "Commands/RepositoryMaintenance/MigrateShipModelPipelineReferencesCommand.cs",
            "RepositoryMaintenance/Unified1eAssetMigrationPlanner.cs",
            "RepositoryMaintenance/Unified1eAssetMigrationModels.cs",
            "RepositoryMaintenance/PrototypeAssetDependencyAuditService.cs",
            "RepositoryMaintenance/PrototypeAssetDependencyModels.cs",
            "RepositoryMaintenance/ShipModelInventoryService.cs",
            "RepositoryMaintenance/ShipModelInventoryModels.cs",
            "RepositoryMaintenance/ObsoleteModelMaintenanceService.cs",
            "RepositoryMaintenance/ObsoleteModelMaintenanceModels.cs",
            "RepositoryMaintenance/RepositoryReferenceScanner.cs"
        },
        StringComparer.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            if (!Directory.Exists(repositoryRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Repository folder was not found: {repositoryRoot}");
            }

            var apply = args.Any(argument =>
                argument.Equals("--apply", StringComparison.OrdinalIgnoreCase));

            var planPath = ResolveOption(
                repositoryRoot,
                args,
                "--plan",
                "_unifiedtoolkit_reports/phase13/" +
                "unified1e-asset-migration/" +
                "unified1e-asset-migration-plan.json");

            if (!File.Exists(planPath))
            {
                throw new FileNotFoundException(
                    "The approved Phase 13 migration plan was not found.",
                    planPath);
            }

            var mappings = LoadMappings(repositoryRoot, planPath);
            if (mappings.Count == 0)
            {
                throw new InvalidDataException(
                    "The migration plan contains no Ready source-to-destination mappings.");
            }

            ValidateDestinations(repositoryRoot, mappings);

            var candidates = DiscoverActiveFiles(repositoryRoot);
            var changes = AnalyseFiles(repositoryRoot, candidates, mappings);

            string? backupRoot = null;
            if (apply && changes.Count > 0)
            {
                backupRoot = CreateBackupRoot(repositoryRoot);
                ApplyChanges(repositoryRoot, backupRoot, changes);
            }

            var reportFolder = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase13",
                "unified1e-asset-rewiring");
            Directory.CreateDirectory(reportFolder);

            var report = new RewireReport
            {
                SchemaVersion = "1.1.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = Normalise(repositoryRoot),
                PlanPath = Normalise(planPath),
                Mode = apply ? "Apply" : "ReportOnly",
                BackupRoot = backupRoot is null
                    ? null
                    : Normalise(backupRoot),
                MappingsLoaded = mappings.Count,
                FilesScanned = candidates.Count,
                FilesChanged = changes.Count,
                ReplacementOccurrences = changes.Sum(change =>
                    change.Replacements.Sum(replacement => replacement.Occurrences)),
                JsonFilesValidated = changes.Count(change =>
                    Path.GetExtension(change.FullPath).Equals(
                        ".json",
                        StringComparison.OrdinalIgnoreCase)),
                Changes = changes
            };

            var jsonPath = Path.Combine(
                reportFolder,
                "unified1e-asset-reference-rewiring.json");
            var csvPath = Path.Combine(
                reportFolder,
                "unified1e-asset-reference-rewiring.csv");
            var markdownPath = Path.Combine(
                reportFolder,
                "UNIFIED1E-ASSET-REFERENCE-REWIRING.md");

            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(report, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, report);
            WriteMarkdown(markdownPath, report);

            Console.WriteLine(
                "UnifiedToolkit First Edition Asset Reference Rewiring");
            Console.WriteLine(
                "======================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Plan:                   {planPath}");
            Console.WriteLine($"Mode:                   {report.Mode}");
            Console.WriteLine();
            Console.WriteLine($"Mappings loaded:        {report.MappingsLoaded}");
            Console.WriteLine($"Authoritative files scanned:{report.FilesScanned}");
            Console.WriteLine($"Files changed:          {report.FilesChanged}");
            Console.WriteLine($"Replacement occurrences:{report.ReplacementOccurrences}");
            Console.WriteLine($"JSON files validated:   {report.JsonFilesValidated}");
            if (backupRoot is not null)
                Console.WriteLine($"Backup:                 {backupRoot}");
            Console.WriteLine($"Report:                 {jsonPath}");
            Console.WriteLine();

            if (!apply)
            {
                Console.WriteLine(
                    "Report-only mode. No active files were modified.");
                Console.WriteLine(
                    "Run the same command with --apply after reviewing the report.");
            }
            else
            {
                Console.WriteLine(
                    "Authoritative asset references were rewired. Derived outputs must now be regenerated. No source assets were moved or deleted.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition asset-reference rewiring failed: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.Error.WriteLine(
            "Usage: rewire-unified1e-asset-references " +
            "<first-edition-repo-folder> [--plan <file>] [--apply]");
    }

    private static List<PathMapping> LoadMappings(
        string repositoryRoot,
        string planPath)
    {
        var root = JsonNode.Parse(File.ReadAllText(planPath))?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the Phase 13 migration plan.");

        var manualReview = root["manualReviewRequired"]?.GetValue<int>() ?? 0;
        var conflicts = root["conflicts"]?.GetValue<int>() ?? 0;
        if (manualReview != 0 || conflicts != 0)
        {
            throw new InvalidDataException(
                "The migration plan is not approved: " +
                $"manual review={manualReview}, conflicts={conflicts}.");
        }

        if (root["entries"] is not JsonArray entries)
            throw new InvalidDataException("Migration plan has no entries array.");

        var mappings = new Dictionary<string, PathMapping>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in entries)
        {
            if (node is not JsonObject entry)
                continue;

            var status = entry["status"]?.GetValue<string>() ?? string.Empty;
            if (!status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                continue;

            var source = Normalise(
                entry["sourcePath"]?.GetValue<string>() ?? string.Empty)
                .TrimEnd('/');
            var destination = Normalise(
                entry["destinationPath"]?.GetValue<string>() ?? string.Empty)
                .TrimEnd('/');

            if (source.Length == 0 || destination.Length == 0)
                continue;

            mappings[source] = new PathMapping(source, destination);
        }

        return mappings.Values
            .OrderByDescending(mapping => mapping.SourcePath.Length)
            .ThenBy(mapping => mapping.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateDestinations(
        string repositoryRoot,
        IReadOnlyList<PathMapping> mappings)
    {
        var missing = new List<string>();

        foreach (var mapping in mappings)
        {
            var sourceFull = SafeRepositoryPath(
                repositoryRoot,
                mapping.SourcePath);
            var destinationFull = SafeRepositoryPath(
                repositoryRoot,
                mapping.DestinationPath);

            if (File.Exists(sourceFull) && !File.Exists(destinationFull))
                missing.Add(mapping.DestinationPath);
            else if (Directory.Exists(sourceFull) && !Directory.Exists(destinationFull))
                missing.Add(mapping.DestinationPath);
        }

        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                "The following approved migration destinations are missing: " +
                string.Join(", ", missing.Take(10)) +
                (missing.Count > 10 ? $" (+{missing.Count - 10} more)" : string.Empty));
        }
    }

    private static List<string> DiscoverActiveFiles(string repositoryRoot)
    {
        // Phase 13C rewires authoritative toolkit and configuration sources
        // only. UKB data, manifests, package plans, validation plans/saves and
        // reports are derived outputs and must be regenerated after rewiring.
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddToolSourceFiles(repositoryRoot, results);

        return results
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddToolSourceFiles(
        string repositoryRoot,
        ISet<string> results)
    {
        var toolRoot = Path.Combine(
            repositoryRoot,
            "tools",
            "UnifiedToolkit");
        if (!Directory.Exists(toolRoot))
            return;

        foreach (var path in Directory.EnumerateFiles(
                     toolRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (!TextExtensions.Contains(Path.GetExtension(path)))
                continue;

            var relative = Normalise(Path.GetRelativePath(toolRoot, path));
            if (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                || ExcludedToolFiles.Contains(relative))
            {
                continue;
            }

            results.Add(Path.GetFullPath(path));
        }
    }

    private static void AddFiles(
        ISet<string> results,
        string folder,
        IReadOnlyCollection<string> extensions)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var path in Directory.EnumerateFiles(
                     folder,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (extensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase))
            {
                results.Add(Path.GetFullPath(path));
            }
        }
    }

    private static void AddFile(ISet<string> results, string path)
    {
        if (File.Exists(path))
            results.Add(Path.GetFullPath(path));
    }

    private static List<FileChange> AnalyseFiles(
        string repositoryRoot,
        IReadOnlyList<string> paths,
        IReadOnlyList<PathMapping> mappings)
    {
        var results = new List<FileChange>();

        foreach (var path in paths)
        {
            string source;
            try
            {
                source = File.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            var replacements = new List<Replacement>();
            var updated = source;

            foreach (var mapping in mappings)
            {
                var occurrences = CountOccurrences(
                    updated,
                    mapping.SourcePath);
                if (occurrences == 0)
                    continue;

                updated = ReplaceOrdinalIgnoreCase(
                    updated,
                    mapping.SourcePath,
                    mapping.DestinationPath);

                replacements.Add(new Replacement
                {
                    SourcePath = mapping.SourcePath,
                    DestinationPath = mapping.DestinationPath,
                    Occurrences = occurrences
                });
            }

            if (replacements.Count == 0
                || updated.Equals(source, StringComparison.Ordinal))
            {
                continue;
            }

            if (Path.GetExtension(path).Equals(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var _ = JsonDocument.Parse(updated);
            }

            results.Add(new FileChange
            {
                RepositoryPath = Normalise(
                    Path.GetRelativePath(repositoryRoot, path)),
                FullPath = path,
                Replacements = replacements,
                UpdatedContent = updated
            });
        }

        return results;
    }

    private static void ApplyChanges(
        string repositoryRoot,
        string backupRoot,
        IReadOnlyList<FileChange> changes)
    {
        foreach (var change in changes)
        {
            var backupPath = Path.Combine(
                backupRoot,
                change.RepositoryPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(change.FullPath, backupPath, overwrite: true);

            if (Path.GetExtension(change.FullPath).Equals(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var _ = JsonDocument.Parse(change.UpdatedContent);
            }

            File.WriteAllText(
                change.FullPath,
                change.UpdatedContent,
                new UTF8Encoding(false));
        }
    }

    private static string CreateBackupRoot(string repositoryRoot)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_backups",
            "unified1e-asset-reference-rewiring",
            timestamp);
        Directory.CreateDirectory(path);
        return path;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;

        while (true)
        {
            var index = source.IndexOf(
                value,
                offset,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return count;

            count++;
            offset = index + value.Length;
        }
    }

    private static string ReplaceOrdinalIgnoreCase(
        string source,
        string oldValue,
        string newValue)
    {
        var builder = new StringBuilder(source.Length);
        var offset = 0;

        while (true)
        {
            var index = source.IndexOf(
                oldValue,
                offset,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                builder.Append(source, offset, source.Length - offset);
                return builder.ToString();
            }

            builder.Append(source, offset, index - offset);
            builder.Append(newValue);
            offset = index + oldValue.Length;
        }
    }

    private static void WriteCsv(string path, RewireReport report)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "RepositoryPath,SourcePath,DestinationPath,Occurrences,Mode");

        foreach (var change in report.Changes)
        {
            foreach (var replacement in change.Replacements)
            {
                writer.WriteLine(string.Join(
                    ",",
                    Csv(change.RepositoryPath),
                    Csv(replacement.SourcePath),
                    Csv(replacement.DestinationPath),
                    replacement.Occurrences,
                    Csv(report.Mode)));
            }
        }
    }

    private static void WriteMarkdown(string path, RewireReport report)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false));

        writer.WriteLine("# Unified First Edition Asset Reference Rewiring");
        writer.WriteLine();
        writer.WriteLine($"- Mode: {report.Mode}");
        writer.WriteLine($"- Mappings loaded: {report.MappingsLoaded}");
        writer.WriteLine($"- Authoritative files scanned: {report.FilesScanned}");
        writer.WriteLine($"- Files changed: {report.FilesChanged}");
        writer.WriteLine(
            $"- Replacement occurrences: {report.ReplacementOccurrences}");
        writer.WriteLine($"- JSON files validated: {report.JsonFilesValidated}");
        writer.WriteLine();

        if (report.Changes.Count == 0)
        {
            writer.WriteLine("No active old-source references were found.");
            return;
        }

        writer.WriteLine("| File | Old path | New path | Count |");
        writer.WriteLine("|---|---|---|---:|");

        foreach (var change in report.Changes)
        {
            foreach (var replacement in change.Replacements)
            {
                writer.WriteLine(
                    $"| `{change.RepositoryPath}` | " +
                    $"`{replacement.SourcePath}` | " +
                    $"`{replacement.DestinationPath}` | " +
                    $"{replacement.Occurrences} |");
            }
        }
    }

    private static string ResolveOption(
        string repositoryRoot,
        string[] args,
        string option,
        string defaultRelative)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                continue;

            return Path.GetFullPath(args[index + 1]);
        }

        return Path.Combine(
            repositoryRoot,
            defaultRelative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string SafeRepositoryPath(
        string repositoryRoot,
        string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Path escapes the repository root: {relativePath}");
        }

        return fullPath;
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Normalise(string value) =>
        value.Replace('\\', '/');

    private sealed record PathMapping(
        string SourcePath,
        string DestinationPath);

    private sealed class RewireReport
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public DateTimeOffset GeneratedUtc { get; set; }
        public string RepositoryRoot { get; set; } = string.Empty;
        public string PlanPath { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string? BackupRoot { get; set; }
        public int MappingsLoaded { get; set; }
        public int FilesScanned { get; set; }
        public int FilesChanged { get; set; }
        public int ReplacementOccurrences { get; set; }
        public int JsonFilesValidated { get; set; }
        public List<FileChange> Changes { get; set; } = [];
    }

    private sealed class FileChange
    {
        public string RepositoryPath { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string FullPath { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string UpdatedContent { get; set; } = string.Empty;

        public List<Replacement> Replacements { get; set; } = [];
    }

    private sealed class Replacement
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public int Occurrences { get; set; }
    }
}
