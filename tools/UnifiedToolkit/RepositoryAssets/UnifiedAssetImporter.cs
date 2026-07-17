using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.RepositoryAssets;

public sealed class UnifiedAssetImporter
{
    private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tga", ".dds", ".svg",
        ".obj", ".mtl", ".fbx", ".dae", ".stl", ".gltf", ".glb", ".3ds", ".blend",
        ".ogg", ".wav", ".mp3", ".flac", ".m4a",
        ".lua", ".json", ".xml", ".yaml", ".yml", ".csv",
        ".pdf", ".txt", ".md",
        ".zip", ".7z", ".rar"
    };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".github", ".vs", ".idea", "bin", "obj", "node_modules",
        "_unifiedtoolkit_reports", "_reports", "reports"
    };

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db", "desktop.ini", ".DS_Store"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UnifiedAssetImportResult Import(
        string unifiedRepositoryRoot,
        string firstEditionRepositoryRoot,
        string? outputFolder,
        bool dryRun)
    {
        ValidateRoot(unifiedRepositoryRoot, "Unified repository");
        ValidateRoot(firstEditionRepositoryRoot, "First Edition repository");

        unifiedRepositoryRoot = Path.GetFullPath(unifiedRepositoryRoot);
        firstEditionRepositoryRoot = Path.GetFullPath(firstEditionRepositoryRoot);

        if (IsSameOrNestedPath(unifiedRepositoryRoot, firstEditionRepositoryRoot)
            || IsSameOrNestedPath(firstEditionRepositoryRoot, unifiedRepositoryRoot))
        {
            throw new InvalidOperationException("The Unified and First Edition repository folders must not contain one another.");
        }

        var destinationRoot = Path.Combine(firstEditionRepositoryRoot, "assets", "source", "unified25");
        var manifestRoot = outputFolder is null
            ? Path.Combine(firstEditionRepositoryRoot, "assets", "manifests")
            : Path.GetFullPath(outputFolder);

        if (!dryRun)
        {
            Directory.CreateDirectory(destinationRoot);
        }

        Directory.CreateDirectory(manifestRoot);

        var allFiles = EnumerateRepositoryFiles(unifiedRepositoryRoot).ToList();
        var selectedFiles = allFiles
            .Where(IsIncludedAsset)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = new List<UnifiedAssetImportEntry>(selectedFiles.Count);
        foreach (var sourcePath in selectedFiles)
        {
            var relativePath = NormalizePath(Path.GetRelativePath(unifiedRepositoryRoot, sourcePath));
            var destinationPath = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var entry = ProcessFile(sourcePath, destinationPath, relativePath, firstEditionRepositoryRoot, dryRun);
            entries.Add(entry);
        }

        var generatedUtc = DateTimeOffset.UtcNow;
        var manifest = new UnifiedAssetImportManifest
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = generatedUtc,
            DryRun = dryRun,
            UnifiedRepositoryRoot = NormalizePath(unifiedRepositoryRoot),
            FirstEditionRepositoryRoot = NormalizePath(firstEditionRepositoryRoot),
            DestinationRoot = NormalizePath(destinationRoot),
            FilesDiscovered = allFiles.Count,
            FilesSelected = selectedFiles.Count,
            Entries = entries
        };

        var manifestPath = Path.Combine(manifestRoot, dryRun
            ? "unified25-import.dry-run.json"
            : "unified25-import.json");
        var reportPath = Path.Combine(manifestRoot, dryRun
            ? "UNIFIED25-ASSET-IMPORT-DRY-RUN.md"
            : "UNIFIED25-ASSET-IMPORT-REPORT.md");

        WriteJson(manifestPath, manifest);
        WriteReport(reportPath, manifest);

        AssetRepositoryCatalogueResult? catalogueResult = null;
        if (!dryRun && entries.All(entry => !entry.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)))
        {
            var catalogueBuilder = new AssetRepositoryCatalogueBuilder();
            catalogueResult = catalogueBuilder.Build(firstEditionRepositoryRoot);
        }

        return BuildResult(manifest, manifestPath, reportPath, catalogueResult?.ManifestRoot);
    }

    private static UnifiedAssetImportEntry ProcessFile(
        string sourcePath,
        string destinationPath,
        string sourceRelativePath,
        string firstEditionRepositoryRoot,
        bool dryRun)
    {
        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            var sourceHash = ComputeSha256(sourcePath);
            var destinationExists = File.Exists(destinationPath);
            string? destinationHash = destinationExists ? ComputeSha256(destinationPath) : null;

            var status = !destinationExists
                ? dryRun ? "would-copy" : "copied"
                : sourceHash.Equals(destinationHash, StringComparison.OrdinalIgnoreCase)
                    ? "unchanged"
                    : dryRun ? "would-update" : "updated";

            if (!dryRun && !status.Equals("unchanged", StringComparison.OrdinalIgnoreCase))
            {
                var destinationDirectory = Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidOperationException($"Cannot determine destination directory for: {destinationPath}");
                Directory.CreateDirectory(destinationDirectory);
                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.SetLastWriteTimeUtc(destinationPath, sourceInfo.LastWriteTimeUtc);
            }

            return new UnifiedAssetImportEntry
            {
                AssetId = $"AST-{sourceHash[..16].ToUpperInvariant()}",
                Kind = DetermineKind(sourcePath),
                Extension = sourceInfo.Extension.ToLowerInvariant(),
                SourceRelativePath = sourceRelativePath,
                SourceAbsolutePath = NormalizePath(sourcePath),
                DestinationRepositoryPath = NormalizePath(Path.GetRelativePath(firstEditionRepositoryRoot, destinationPath)),
                SizeBytes = sourceInfo.Length,
                Sha256 = sourceHash,
                SourceLastWriteTimeUtc = sourceInfo.LastWriteTimeUtc,
                Status = status,
                Error = null
            };
        }
        catch (Exception exception)
        {
            return new UnifiedAssetImportEntry
            {
                Kind = DetermineKind(sourcePath),
                Extension = Path.GetExtension(sourcePath).ToLowerInvariant(),
                SourceRelativePath = sourceRelativePath,
                SourceAbsolutePath = NormalizePath(sourcePath),
                DestinationRepositoryPath = NormalizePath(Path.GetRelativePath(firstEditionRepositoryRoot, destinationPath)),
                Status = "failed",
                Error = exception.Message
            };
        }
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(current);
                files = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (!IgnoredDirectoryNames.Contains(Path.GetFileName(directory)))
                {
                    pending.Push(directory);
                }
            }

            foreach (var file in files)
            {
                if (!IgnoredFileNames.Contains(Path.GetFileName(file)))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsIncludedAsset(string path)
        => IncludedExtensions.Contains(Path.GetExtension(path));

    private static void ValidateRoot(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{description} folder is required.");
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{description} folder does not exist: {Path.GetFullPath(path)}");
        }
    }

    private static bool IsSameOrNestedPath(string candidate, string possibleParent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedParent = Path.GetFullPath(possibleParent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string DetermineKind(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tga" or ".dds" or ".svg" => "image",
            ".obj" or ".mtl" or ".fbx" or ".dae" or ".stl" or ".gltf" or ".glb" or ".3ds" or ".blend" => "model",
            ".ogg" or ".wav" or ".mp3" or ".flac" or ".m4a" => "audio",
            ".lua" => "lua",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".csv" => "csv",
            ".pdf" => "pdf",
            ".txt" or ".md" => "text",
            ".zip" or ".7z" or ".rar" => "archive",
            _ => "other"
        };
    }

    private static UnifiedAssetImportResult BuildResult(
        UnifiedAssetImportManifest manifest,
        string manifestPath,
        string reportPath,
        string? catalogueManifestRoot)
    {
        return new UnifiedAssetImportResult
        {
            DryRun = manifest.DryRun,
            DestinationRoot = manifest.DestinationRoot,
            ManifestPath = manifestPath,
            ReportPath = reportPath,
            CatalogueManifestRoot = catalogueManifestRoot,
            FilesDiscovered = manifest.FilesDiscovered,
            FilesSelected = manifest.FilesSelected,
            FilesCopied = manifest.Entries.Count(entry => entry.Status is "copied" or "would-copy"),
            FilesUpdated = manifest.Entries.Count(entry => entry.Status is "updated" or "would-update"),
            FilesUnchanged = manifest.Entries.Count(entry => entry.Status == "unchanged"),
            FilesSkipped = manifest.Entries.Count(entry => entry.Status == "skipped"),
            FilesFailed = manifest.Entries.Count(entry => entry.Status == "failed"),
            BytesSelected = manifest.Entries.Sum(entry => entry.SizeBytes)
        };
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void WriteReport(string path, UnifiedAssetImportManifest manifest)
    {
        var byKind = manifest.Entries
            .GroupBy(entry => entry.Kind, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var byStatus = manifest.Entries
            .GroupBy(entry => entry.Status, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(manifest.DryRun
            ? "# Unified 2.5 Asset Import Dry Run"
            : "# Unified 2.5 Asset Import Report");
        writer.WriteLine();
        writer.WriteLine($"Generated: {manifest.GeneratedUtc:O}");
        writer.WriteLine();
        writer.WriteLine($"- Unified repository: `{manifest.UnifiedRepositoryRoot}`");
        writer.WriteLine($"- Destination: `{manifest.DestinationRoot}`");
        writer.WriteLine($"- Files discovered: {manifest.FilesDiscovered}");
        writer.WriteLine($"- Files selected: {manifest.FilesSelected}");
        writer.WriteLine($"- Bytes selected: {manifest.Entries.Sum(entry => entry.SizeBytes):N0}");
        writer.WriteLine();
        writer.WriteLine("## Status");
        writer.WriteLine();
        foreach (var group in byStatus)
        {
            writer.WriteLine($"- {group.Key}: {group.Count()}");
        }

        writer.WriteLine();
        writer.WriteLine("## File kinds");
        writer.WriteLine();
        foreach (var group in byKind)
        {
            writer.WriteLine($"- {group.Key}: {group.Count()}");
        }

        var failures = manifest.Entries.Where(entry => entry.Status == "failed").ToList();
        if (failures.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Failures");
            writer.WriteLine();
            foreach (var failure in failures)
            {
                writer.WriteLine($"- `{failure.SourceRelativePath}`: {failure.Error}");
            }
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class UnifiedAssetImportResult
{
    public bool DryRun { get; init; }
    public string DestinationRoot { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string? CatalogueManifestRoot { get; init; }
    public int FilesDiscovered { get; init; }
    public int FilesSelected { get; init; }
    public int FilesCopied { get; init; }
    public int FilesUpdated { get; init; }
    public int FilesUnchanged { get; init; }
    public int FilesSkipped { get; init; }
    public int FilesFailed { get; init; }
    public long BytesSelected { get; init; }
}

public sealed class UnifiedAssetImportManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public bool DryRun { get; init; }
    public string UnifiedRepositoryRoot { get; init; } = string.Empty;
    public string FirstEditionRepositoryRoot { get; init; } = string.Empty;
    public string DestinationRoot { get; init; } = string.Empty;
    public int FilesDiscovered { get; init; }
    public int FilesSelected { get; init; }
    public List<UnifiedAssetImportEntry> Entries { get; init; } = new();
}

public sealed class UnifiedAssetImportEntry
{
    public string AssetId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string SourceRelativePath { get; init; } = string.Empty;
    public string SourceAbsolutePath { get; init; } = string.Empty;
    public string DestinationRepositoryPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTime SourceLastWriteTimeUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
}
