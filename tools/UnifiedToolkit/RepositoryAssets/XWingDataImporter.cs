using System.Security.Cryptography;
using System.Text.Json;

namespace UnifiedToolkit.RepositoryAssets;

public sealed class XWingDataImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyList<ImportArea> Areas = new[]
    {
        new ImportArea("images", "assets/source/xwing-data/images", "asset"),
        new ImportArea("data", "source/xwing-data/data", "reference-data"),
        new ImportArea("schemas", "source/xwing-data/schemas", "schema")
    };

    public XWingDataImportResult Import(
        string xwingDataRoot,
        string firstEditionRepositoryRoot,
        bool dryRun = false)
    {
        if (string.IsNullOrWhiteSpace(xwingDataRoot))
            throw new ArgumentException("The xwing-data repository root is required.", nameof(xwingDataRoot));
        if (string.IsNullOrWhiteSpace(firstEditionRepositoryRoot))
            throw new ArgumentException("The First Edition repository root is required.", nameof(firstEditionRepositoryRoot));

        xwingDataRoot = Path.GetFullPath(xwingDataRoot);
        firstEditionRepositoryRoot = Path.GetFullPath(firstEditionRepositoryRoot);

        if (!Directory.Exists(xwingDataRoot))
            throw new DirectoryNotFoundException($"xwing-data repository folder does not exist: {xwingDataRoot}");
        if (!Directory.Exists(firstEditionRepositoryRoot))
            throw new DirectoryNotFoundException($"First Edition repository folder does not exist: {firstEditionRepositoryRoot}");

        ValidateSource(xwingDataRoot);

        var entries = new List<XWingDataImportEntry>();
        foreach (var area in Areas)
        {
            var sourceArea = Path.Combine(xwingDataRoot, area.SourceFolder);
            if (!Directory.Exists(sourceArea))
                continue;

            foreach (var sourcePath in Directory.EnumerateFiles(sourceArea, "*", SearchOption.AllDirectories)
                         .Where(path => !IsIgnored(path))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var sourceRelativePath = Normalize(Path.GetRelativePath(xwingDataRoot, sourcePath));
                var areaRelativePath = Path.GetRelativePath(sourceArea, sourcePath);
                var destinationRelativePath = Normalize(Path.Combine(area.DestinationFolder, areaRelativePath));
                var destinationPath = Path.Combine(firstEditionRepositoryRoot, destinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var sourceHash = ComputeSha256(sourcePath);
                var status = DetermineStatus(sourcePath, destinationPath, sourceHash);
                string? error = null;

                if (!dryRun && status is "copied" or "updated")
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        File.Copy(sourcePath, destinationPath, overwrite: true);
                        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
                    }
                    catch (Exception exception)
                    {
                        status = "failed";
                        error = exception.Message;
                    }
                }

                var info = new FileInfo(sourcePath);
                entries.Add(new XWingDataImportEntry
                {
                    SourceRelativePath = sourceRelativePath,
                    DestinationRepositoryPath = destinationRelativePath,
                    Category = area.Category,
                    EntityType = InferEntityType(sourceRelativePath),
                    Extension = info.Extension.ToLowerInvariant(),
                    SizeBytes = info.Length,
                    Sha256 = sourceHash,
                    Status = dryRun && status is "copied" or "updated" ? $"would-{status}" : status,
                    Error = error
                });
            }
        }

        var outputRoot = Path.Combine(firstEditionRepositoryRoot, "source", "xwing-data");
        Directory.CreateDirectory(outputRoot);
        var manifestPath = Path.Combine(outputRoot, "import-manifest.json");
        var reportPath = Path.Combine(outputRoot, "IMPORT-REPORT.md");

        var manifest = new XWingDataImportManifest
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceRepository = Normalize(xwingDataRoot),
            DestinationRepository = Normalize(firstEditionRepositoryRoot),
            DryRun = dryRun,
            Entries = entries
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteReport(reportPath, manifest);

        return new XWingDataImportResult
        {
            SourceRoot = xwingDataRoot,
            DestinationRoot = firstEditionRepositoryRoot,
            ManifestPath = manifestPath,
            ReportPath = reportPath,
            FilesDiscovered = entries.Count,
            FilesCopied = entries.Count(entry => entry.Status is "copied" or "would-copied"),
            FilesUpdated = entries.Count(entry => entry.Status is "updated" or "would-updated"),
            FilesUnchanged = entries.Count(entry => entry.Status == "unchanged"),
            FilesFailed = entries.Count(entry => entry.Status == "failed"),
            AssetFiles = entries.Count(entry => entry.Category == "asset"),
            ReferenceDataFiles = entries.Count(entry => entry.Category == "reference-data"),
            SchemaFiles = entries.Count(entry => entry.Category == "schema"),
            BytesSelected = entries.Where(entry => entry.Status is "copied" or "updated" or "would-copied" or "would-updated").Sum(entry => entry.SizeBytes)
        };
    }

    private static void ValidateSource(string sourceRoot)
    {
        var missing = Areas
            .Where(area => !Directory.Exists(Path.Combine(sourceRoot, area.SourceFolder)))
            .Select(area => area.SourceFolder)
            .ToList();

        if (missing.Count == Areas.Count)
            throw new InvalidOperationException("The selected folder does not appear to be an xwing-data repository. Expected images, data, or schemas folders.");
    }

    private static string DetermineStatus(string sourcePath, string destinationPath, string sourceHash)
    {
        if (!File.Exists(destinationPath))
            return "copied";

        var destinationInfo = new FileInfo(destinationPath);
        var sourceInfo = new FileInfo(sourcePath);
        if (destinationInfo.Length == sourceInfo.Length
            && ComputeSha256(destinationPath).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
            return "unchanged";

        return "updated";
    }

    private static string InferEntityType(string relativePath)
    {
        var firstSegment = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .FirstOrDefault()?.ToLowerInvariant();

        return firstSegment switch
        {
            "pilots" => "pilot",
            "upgrades" => "upgrade",
            "conditions" => "condition",
            "damage-decks" => "damage-deck",
            "factions" => "faction",
            "reference-cards" => "reference-card",
            "sources" => "source",
            _ => "reference"
        };
    }

    private static bool IsIgnored(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static void WriteReport(string path, XWingDataImportManifest manifest)
    {
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("# xwing-data Import Report");
        writer.WriteLine();
        writer.WriteLine($"Generated: {manifest.GeneratedUtc:O}");
        writer.WriteLine();
        writer.WriteLine($"- Source: `{manifest.SourceRepository}`");
        writer.WriteLine($"- Destination: `{manifest.DestinationRepository}`");
        writer.WriteLine($"- Mode: {(manifest.DryRun ? "Dry run" : "Import")}");
        writer.WriteLine($"- Files discovered: {manifest.Entries.Count}");
        writer.WriteLine($"- Asset images: {manifest.Entries.Count(entry => entry.Category == "asset")}");
        writer.WriteLine($"- Reference-data files: {manifest.Entries.Count(entry => entry.Category == "reference-data")}");
        writer.WriteLine($"- Schema files: {manifest.Entries.Count(entry => entry.Category == "schema")}");
        writer.WriteLine();
        writer.WriteLine("## Status");
        writer.WriteLine();
        foreach (var group in manifest.Entries.GroupBy(entry => entry.Status, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key))
            writer.WriteLine($"- {group.Key}: {group.Count()}");
        writer.WriteLine();
        writer.WriteLine("## Imported areas");
        writer.WriteLine();
        writer.WriteLine("- `images` → `assets/source/xwing-data/images` (catalogued game artwork)");
        writer.WriteLine("- `data` → `source/xwing-data/data` (First Edition reference dataset)");
        writer.WriteLine("- `schemas` → `source/xwing-data/schemas` (reference-data validation schemas)");
        writer.WriteLine();
        writer.WriteLine("Root development files and upstream tests are intentionally not imported.");
    }

    private sealed record ImportArea(string SourceFolder, string DestinationFolder, string Category);
}

public sealed class XWingDataImportManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceRepository { get; init; } = string.Empty;
    public string DestinationRepository { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public List<XWingDataImportEntry> Entries { get; init; } = new();
}

public sealed class XWingDataImportEntry
{
    public string SourceRelativePath { get; init; } = string.Empty;
    public string DestinationRepositoryPath { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public sealed class XWingDataImportResult
{
    public string SourceRoot { get; init; } = string.Empty;
    public string DestinationRoot { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public int FilesDiscovered { get; init; }
    public int FilesCopied { get; init; }
    public int FilesUpdated { get; init; }
    public int FilesUnchanged { get; init; }
    public int FilesFailed { get; init; }
    public int AssetFiles { get; init; }
    public int ReferenceDataFiles { get; init; }
    public int SchemaFiles { get; init; }
    public long BytesSelected { get; init; }
}
