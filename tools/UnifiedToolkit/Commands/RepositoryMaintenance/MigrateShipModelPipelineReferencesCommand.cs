using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

/// <summary>
/// Replaces known stale ship-model selections in active pipeline JSON files.
///
/// The command is report-only unless --apply is supplied. Apply mode creates a
/// timestamped backup before modifying any file. It never moves or deletes OBJ
/// files and deliberately does not rewrite historical model-selection,
/// model-cleanup, inventory, or general asset-catalogue reports.
/// </summary>
public static class MigrateShipModelPipelineReferencesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyList<ModelPathMigration> Migrations =
    [
        new(
            "assets/source/unified25/assets/ships-v2/medium/" +
            "firesprayclasspatrolcraft/firesprayV3-canopy.obj",
            "assets/source/unified25/assets/ships-v2/medium/" +
            "firesprayclasspatrolcraft/firesprayV2.obj",
            "Firespray-31"),

        new(
            "assets/source/unified25/assets/ships-v2/small/" +
            "tiefofighter/Tie_FO.obj",
            "assets/source/unified25/assets/ships-v2/small/" +
            "tiefofighter/TieFOv2.obj",
            "TIE/fo Fighter"),

        new(
            "assets/source/unified25/assets/ships-v2/small/" +
            "tiesffighter/TieSF.obj",
            "assets/source/unified25/assets/ships-v2/small/" +
            "tiesffighter/TieSFv2.obj",
            "TIE/sf Fighter"),

        new(
            "assets/source/unified25/assets/ships-v2/small/" +
            "tievnsilencer/Tie_VN.obj",
            "assets/source/unified25/assets/ships-v2/small/" +
            "tievnsilencer/Tie_VN2.obj",
            "TIE/vn Silencer")
    ];

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

            ValidateReplacementModels(repositoryRoot);

            var candidateFiles = DiscoverActivePipelineFiles(repositoryRoot);
            var changes = AnalyseFiles(repositoryRoot, candidateFiles);

            string? backupRoot = null;
            if (apply && changes.Count > 0)
            {
                backupRoot = CreateBackupRoot(repositoryRoot);
                ApplyChanges(repositoryRoot, backupRoot, changes);
            }

            var reportFolder = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "model-selection");
            Directory.CreateDirectory(reportFolder);

            var report = new PipelineModelReferenceMigrationReport
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = Normalise(repositoryRoot),
                Mode = apply ? "Apply" : "ReportOnly",
                BackupRoot = backupRoot is null
                    ? null
                    : Normalise(backupRoot),
                FilesScanned = candidateFiles.Count,
                FilesChanged = changes.Count,
                Replacements = changes.Sum(change => change.Replacements.Count),
                Changes = changes
            };

            var reportPath = Path.Combine(
                reportFolder,
                "ship-model-pipeline-reference-migration.json");
            var csvPath = Path.Combine(
                reportFolder,
                "ship-model-pipeline-reference-migration.csv");
            var markdownPath = Path.Combine(
                reportFolder,
                "SHIP-MODEL-PIPELINE-REFERENCE-MIGRATION.md");

            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, report);
            WriteMarkdown(markdownPath, report);

            Console.WriteLine(
                "UnifiedToolkit Ship Model Pipeline Reference Migration");
            Console.WriteLine(
                "======================================================");
            Console.WriteLine($"Repository:          {repositoryRoot}");
            Console.WriteLine($"Mode:                {report.Mode}");
            Console.WriteLine($"Files scanned:       {report.FilesScanned}");
            Console.WriteLine($"Files changed:       {report.FilesChanged}");
            Console.WriteLine($"Replacements:        {report.Replacements}");
            if (backupRoot is not null)
                Console.WriteLine($"Backup:              {backupRoot}");
            Console.WriteLine($"Report:              {reportPath}");
            Console.WriteLine();

            if (!apply)
            {
                Console.WriteLine(
                    "Report-only mode. No pipeline files were modified.");
                Console.WriteLine(
                    "Run the same command with --apply after reviewing the report.");
            }
            else
            {
                Console.WriteLine(
                    "Active pipeline references were updated. No OBJ files were moved or deleted.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Ship-model pipeline-reference migration failed: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.Error.WriteLine(
            "Usage: migrate-ship-model-pipeline-references " +
            "<first-edition-repo-folder> [--apply]");
    }

    private static void ValidateReplacementModels(string repositoryRoot)
    {
        foreach (var migration in Migrations)
        {
            var replacementPath = Path.Combine(
                repositoryRoot,
                migration.NewPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            if (!File.Exists(replacementPath))
            {
                throw new FileNotFoundException(
                    $"{migration.ShipName} replacement OBJ was not found.",
                    replacementPath);
            }
        }
    }

    private static List<string> DiscoverActivePipelineFiles(
        string repositoryRoot)
    {
        var results = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        AddFile(
            results,
            Path.Combine(repositoryRoot, "ukb", "ship-links.json"));

        AddJsonFolder(
            results,
            Path.Combine(
                repositoryRoot,
                "assets", "generated", "validation", "plans"));

        AddJsonFolder(
            results,
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase11",
                "ship-package-planning"));

        AddJsonFolder(
            results,
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12b"));

        return results
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddFile(
        ISet<string> results,
        string path)
    {
        if (File.Exists(path))
            results.Add(Path.GetFullPath(path));
    }

    private static void AddJsonFolder(
        ISet<string> results,
        string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var path in Directory.EnumerateFiles(
                     folder,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            results.Add(Path.GetFullPath(path));
        }
    }

    private static List<PipelineFileChange> AnalyseFiles(
        string repositoryRoot,
        IReadOnlyList<string> paths)
    {
        var results = new List<PipelineFileChange>();

        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            var replacements = new List<PipelineReplacement>();

            foreach (var migration in Migrations)
            {
                var count = CountOccurrences(
                    source,
                    migration.OldPath);

                if (count == 0)
                    continue;

                replacements.Add(new PipelineReplacement
                {
                    ShipName = migration.ShipName,
                    OldPath = migration.OldPath,
                    NewPath = migration.NewPath,
                    Occurrences = count
                });
            }

            if (replacements.Count == 0)
                continue;

            results.Add(new PipelineFileChange
            {
                RepositoryPath = Normalise(
                    Path.GetRelativePath(repositoryRoot, path)),
                FullPath = path,
                Replacements = replacements
            });
        }

        return results;
    }

    private static int CountOccurrences(
        string source,
        string value)
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

    private static string CreateBackupRoot(string repositoryRoot)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var root = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_backups",
            "ship-model-pipeline-reference-migration",
            stamp);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ApplyChanges(
        string repositoryRoot,
        string backupRoot,
        IReadOnlyList<PipelineFileChange> changes)
    {
        foreach (var change in changes)
        {
            var source = File.ReadAllText(change.FullPath);
            var updated = source;

            foreach (var replacement in change.Replacements)
            {
                updated = ReplaceOrdinalIgnoreCase(
                    updated,
                    replacement.OldPath,
                    replacement.NewPath);
            }

            if (updated.Equals(source, StringComparison.Ordinal))
                continue;

            // Confirm the updated content remains valid JSON before replacing
            // the active pipeline file.
            using (JsonDocument.Parse(updated))
            {
            }

            var backupPath = Path.Combine(
                backupRoot,
                change.RepositoryPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(
                Path.GetDirectoryName(backupPath)!);
            File.Copy(
                change.FullPath,
                backupPath,
                overwrite: true);

            File.WriteAllText(
                change.FullPath,
                updated,
                new UTF8Encoding(false));
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

    private static void WriteCsv(
        string path,
        PipelineModelReferenceMigrationReport report)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "RepositoryPath,ShipName,OldPath,NewPath,Occurrences,Mode");

        foreach (var change in report.Changes)
        {
            foreach (var replacement in change.Replacements)
            {
                writer.WriteLine(string.Join(
                    ",",
                    Csv(change.RepositoryPath),
                    Csv(replacement.ShipName),
                    Csv(replacement.OldPath),
                    Csv(replacement.NewPath),
                    replacement.Occurrences,
                    Csv(report.Mode)));
            }
        }
    }

    private static void WriteMarkdown(
        string path,
        PipelineModelReferenceMigrationReport report)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false));

        writer.WriteLine("# Ship Model Pipeline Reference Migration");
        writer.WriteLine();
        writer.WriteLine($"- Mode: {report.Mode}");
        writer.WriteLine($"- Files scanned: {report.FilesScanned}");
        writer.WriteLine($"- Files changed: {report.FilesChanged}");
        writer.WriteLine($"- Replacements: {report.Replacements}");
        writer.WriteLine();

        if (report.Changes.Count == 0)
        {
            writer.WriteLine("No stale active pipeline references were found.");
            return;
        }

        writer.WriteLine("| File | Ship | Old OBJ | New OBJ | Count |");
        writer.WriteLine("|---|---|---|---|---:|");

        foreach (var change in report.Changes)
        {
            foreach (var replacement in change.Replacements)
            {
                writer.WriteLine(
                    $"| `{change.RepositoryPath}` | " +
                    $"{replacement.ShipName} | " +
                    $"`{replacement.OldPath}` | " +
                    $"`{replacement.NewPath}` | " +
                    $"{replacement.Occurrences} |");
            }
        }
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Normalise(string path) =>
        path.Replace('\\', '/');

    private sealed record ModelPathMigration(
        string OldPath,
        string NewPath,
        string ShipName);

    private sealed class PipelineModelReferenceMigrationReport
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public DateTimeOffset GeneratedUtc { get; set; }
        public string RepositoryRoot { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string? BackupRoot { get; set; }
        public int FilesScanned { get; set; }
        public int FilesChanged { get; set; }
        public int Replacements { get; set; }
        public List<PipelineFileChange> Changes { get; set; } = [];
    }

    private sealed class PipelineFileChange
    {
        public string RepositoryPath { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string FullPath { get; set; } = string.Empty;

        public List<PipelineReplacement> Replacements { get; set; } = [];
    }

    private sealed class PipelineReplacement
    {
        public string ShipName { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
        public int Occurrences { get; set; }
    }
}
