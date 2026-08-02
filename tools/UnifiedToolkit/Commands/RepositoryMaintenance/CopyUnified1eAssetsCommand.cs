using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

/// <summary>
/// Phase 13B: copies every Ready entry from the approved First Edition asset
/// migration plan into assets/source/unified1e. The command is report-only
/// unless --apply is supplied. Existing differing files are never overwritten.
/// No source files are moved, changed, or deleted.
/// </summary>
public static class CopyUnified1eAssetsCommand
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
                throw new FileNotFoundException("Migration plan was not found.", planPath);

            var plan = JsonNode.Parse(File.ReadAllText(planPath))?.AsObject()
                ?? throw new InvalidDataException("Could not parse the migration plan.");

            var manualReview = plan["manualReviewRequired"]?.GetValue<int>() ?? 0;
            var conflicts = plan["conflicts"]?.GetValue<int>() ?? 0;
            if (manualReview != 0 || conflicts != 0)
            {
                throw new InvalidDataException(
                    "The migration plan is not approved: " +
                    $"manual review={manualReview}, conflicts={conflicts}.");
            }

            if (plan["entries"] is not JsonArray entries)
                throw new InvalidDataException("Migration plan has no entries array.");

            var operations = ExpandOperations(repositoryRoot, entries);
            var results = Execute(repositoryRoot, operations, apply);

            var reportFolder = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase13",
                "unified1e-asset-migration");
            Directory.CreateDirectory(reportFolder);

            var report = new CopyReport
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = Normalise(repositoryRoot),
                PlanPath = Normalise(planPath),
                Mode = apply ? "Apply" : "ReportOnly",
                PlannedFiles = results.Count,
                Copied = results.Count(item => item.Status == "Copied"),
                IdenticalExisting = results.Count(item => item.Status == "IdenticalExisting"),
                WouldCopy = results.Count(item => item.Status == "WouldCopy"),
                Conflicts = results.Count(item => item.Status == "ConflictExistingDifferent"),
                MissingSources = results.Count(item => item.Status == "MissingSource"),
                Errors = results.Count(item => item.Status == "Error"),
                BytesCopied = results
                    .Where(item => item.Status == "Copied")
                    .Sum(item => item.SizeBytes),
                Results = results
            };

            var jsonPath = Path.Combine(reportFolder, "unified1e-asset-copy.json");
            var csvPath = Path.Combine(reportFolder, "unified1e-asset-copy.csv");
            var markdownPath = Path.Combine(reportFolder, "UNIFIED1E-ASSET-COPY.md");

            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(report, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, report);
            WriteMarkdown(markdownPath, report);

            Console.WriteLine("UnifiedToolkit First Edition Asset Copy");
            Console.WriteLine("=======================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Plan:                   {planPath}");
            Console.WriteLine($"Mode:                   {report.Mode}");
            Console.WriteLine();
            Console.WriteLine($"Planned files:          {report.PlannedFiles}");
            Console.WriteLine($"Would copy:             {report.WouldCopy}");
            Console.WriteLine($"Copied:                 {report.Copied}");
            Console.WriteLine($"Identical existing:     {report.IdenticalExisting}");
            Console.WriteLine($"Conflicts:              {report.Conflicts}");
            Console.WriteLine($"Missing sources:        {report.MissingSources}");
            Console.WriteLine($"Errors:                 {report.Errors}");
            Console.WriteLine($"Report:                 {jsonPath}");
            Console.WriteLine();

            if (!apply)
            {
                Console.WriteLine("Report-only mode. No files were copied.");
                Console.WriteLine("Run the same command with --apply to perform the copy.");
            }
            else
            {
                Console.WriteLine(
                    "Copy-only migration completed. No source files were moved, changed, or deleted.");
            }

            return report.Conflicts == 0
                   && report.MissingSources == 0
                   && report.Errors == 0
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"First Edition asset copy failed: {ex.Message}");
            return 1;
        }
    }

    private static List<CopyOperation> ExpandOperations(
        string repositoryRoot,
        JsonArray entries)
    {
        var operations = new Dictionary<string, CopyOperation>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in entries)
        {
            if (node is not JsonObject entry)
                continue;

            var status = entry["status"]?.GetValue<string>() ?? string.Empty;
            if (!status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                continue;

            var kind = entry["kind"]?.GetValue<string>() ?? string.Empty;
            var sourceRelative = Normalise(
                entry["sourcePath"]?.GetValue<string>() ?? string.Empty);
            var destinationRelative = Normalise(
                entry["destinationPath"]?.GetValue<string>() ?? string.Empty);

            if (sourceRelative.Length == 0 || destinationRelative.Length == 0)
                continue;

            var sourceFull = SafeRepositoryPath(repositoryRoot, sourceRelative);
            var destinationFull = SafeRepositoryPath(repositoryRoot, destinationRelative);

            if (Directory.Exists(sourceFull))
            {
                foreach (var sourceFile in Directory.EnumerateFiles(
                             sourceFull,
                             "*",
                             SearchOption.AllDirectories))
                {
                    var suffix = Path.GetRelativePath(sourceFull, sourceFile);
                    var destinationFile = Path.Combine(destinationFull, suffix);
                    AddOperation(
                        operations,
                        repositoryRoot,
                        kind,
                        sourceFile,
                        destinationFile);
                }
            }
            else
            {
                AddOperation(
                    operations,
                    repositoryRoot,
                    kind,
                    sourceFull,
                    destinationFull);
            }
        }

        return operations.Values
            .OrderBy(item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddOperation(
        IDictionary<string, CopyOperation> operations,
        string repositoryRoot,
        string kind,
        string sourceFull,
        string destinationFull)
    {
        var destinationRelative = Normalise(
            Path.GetRelativePath(repositoryRoot, destinationFull));

        if (operations.TryGetValue(destinationRelative, out var existing))
        {
            var sourceRelative = Normalise(
                Path.GetRelativePath(repositoryRoot, sourceFull));
            if (!existing.SourcePath.Equals(
                    sourceRelative,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Multiple migration sources target the same destination: " +
                    destinationRelative);
            }
            return;
        }

        operations[destinationRelative] = new CopyOperation
        {
            Kind = kind,
            SourceFullPath = sourceFull,
            DestinationFullPath = destinationFull,
            SourcePath = Normalise(Path.GetRelativePath(repositoryRoot, sourceFull)),
            DestinationPath = destinationRelative
        };
    }

    private static List<CopyResult> Execute(
        string repositoryRoot,
        IReadOnlyList<CopyOperation> operations,
        bool apply)
    {
        var results = new List<CopyResult>(operations.Count);

        foreach (var operation in operations)
        {
            try
            {
                if (!File.Exists(operation.SourceFullPath))
                {
                    results.Add(CreateResult(operation, "MissingSource"));
                    continue;
                }

                var sourceInfo = new FileInfo(operation.SourceFullPath);
                var sourceHash = Hash(operation.SourceFullPath);

                if (File.Exists(operation.DestinationFullPath))
                {
                    var destinationHash = Hash(operation.DestinationFullPath);
                    results.Add(new CopyResult
                    {
                        Kind = operation.Kind,
                        SourcePath = operation.SourcePath,
                        DestinationPath = operation.DestinationPath,
                        Status = sourceHash.Equals(
                            destinationHash,
                            StringComparison.OrdinalIgnoreCase)
                            ? "IdenticalExisting"
                            : "ConflictExistingDifferent",
                        SizeBytes = sourceInfo.Length,
                        SourceSha256 = sourceHash,
                        DestinationSha256 = destinationHash
                    });
                    continue;
                }

                if (!apply)
                {
                    results.Add(new CopyResult
                    {
                        Kind = operation.Kind,
                        SourcePath = operation.SourcePath,
                        DestinationPath = operation.DestinationPath,
                        Status = "WouldCopy",
                        SizeBytes = sourceInfo.Length,
                        SourceSha256 = sourceHash
                    });
                    continue;
                }

                var parent = Path.GetDirectoryName(operation.DestinationFullPath)
                    ?? throw new InvalidDataException(
                        "Destination file has no parent directory.");
                Directory.CreateDirectory(parent);
                File.Copy(
                    operation.SourceFullPath,
                    operation.DestinationFullPath,
                    overwrite: false);

                var copiedHash = Hash(operation.DestinationFullPath);
                if (!sourceHash.Equals(copiedHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(operation.DestinationFullPath);
                    throw new InvalidDataException(
                        "Copied file failed SHA-256 verification.");
                }

                results.Add(new CopyResult
                {
                    Kind = operation.Kind,
                    SourcePath = operation.SourcePath,
                    DestinationPath = operation.DestinationPath,
                    Status = "Copied",
                    SizeBytes = sourceInfo.Length,
                    SourceSha256 = sourceHash,
                    DestinationSha256 = copiedHash
                });
            }
            catch (Exception ex)
            {
                var result = CreateResult(operation, "Error");
                result.Error = ex.Message;
                results.Add(result);
            }
        }

        return results;
    }

    private static CopyResult CreateResult(CopyOperation operation, string status) =>
        new()
        {
            Kind = operation.Kind,
            SourcePath = operation.SourcePath,
            DestinationPath = operation.DestinationPath,
            Status = status
        };

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SafeRepositoryPath(
        string repositoryRoot,
        string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Plan path must be repository-relative: {relativePath}");

        var fullPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = repositoryRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Plan path escapes the repository: {relativePath}");

        return fullPath;
    }

    private static string ResolveOption(
        string repositoryRoot,
        string[] args,
        string option,
        string fallbackRelative)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                continue;

            return Path.GetFullPath(Path.IsPathRooted(args[index + 1])
                ? args[index + 1]
                : Path.Combine(repositoryRoot, args[index + 1]));
        }

        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            fallbackRelative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void WriteCsv(string path, CopyReport report)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "Kind,SourcePath,DestinationPath,Status,SizeBytes," +
            "SourceSha256,DestinationSha256,Error");

        foreach (var item in report.Results)
        {
            writer.WriteLine(string.Join(",",
                Csv(item.Kind),
                Csv(item.SourcePath),
                Csv(item.DestinationPath),
                Csv(item.Status),
                item.SizeBytes,
                Csv(item.SourceSha256),
                Csv(item.DestinationSha256),
                Csv(item.Error)));
        }
    }

    private static void WriteMarkdown(string path, CopyReport report)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Unified First Edition Asset Copy");
        writer.WriteLine();
        writer.WriteLine($"- Mode: {report.Mode}");
        writer.WriteLine($"- Planned files: {report.PlannedFiles}");
        writer.WriteLine($"- Would copy: {report.WouldCopy}");
        writer.WriteLine($"- Copied: {report.Copied}");
        writer.WriteLine($"- Identical existing: {report.IdenticalExisting}");
        writer.WriteLine($"- Conflicts: {report.Conflicts}");
        writer.WriteLine($"- Missing sources: {report.MissingSources}");
        writer.WriteLine($"- Errors: {report.Errors}");
        writer.WriteLine();
        writer.WriteLine("No source files are moved, changed, or deleted by this command.");
    }

    private static string Csv(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Normalise(string value) => value.Replace('\\', '/');

    private static void ShowUsage() => Console.Error.WriteLine(
        "Usage: copy-unified1e-assets <first-edition-repo-folder> " +
        "[--plan <file>] [--apply]");

    private sealed class CopyOperation
    {
        public string Kind { get; set; } = string.Empty;
        public string SourceFullPath { get; set; } = string.Empty;
        public string DestinationFullPath { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
    }

    private sealed class CopyResult
    {
        public string Kind { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SourceSha256 { get; set; } = string.Empty;
        public string DestinationSha256 { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    private sealed class CopyReport
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public DateTimeOffset GeneratedUtc { get; set; }
        public string RepositoryRoot { get; set; } = string.Empty;
        public string PlanPath { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public int PlannedFiles { get; set; }
        public int WouldCopy { get; set; }
        public int Copied { get; set; }
        public int IdenticalExisting { get; set; }
        public int Conflicts { get; set; }
        public int MissingSources { get; set; }
        public int Errors { get; set; }
        public long BytesCopied { get; set; }
        public List<CopyResult> Results { get; set; } = [];
    }
}
