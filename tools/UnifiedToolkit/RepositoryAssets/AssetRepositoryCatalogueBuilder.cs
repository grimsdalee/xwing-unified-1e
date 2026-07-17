using System.Security.Cryptography;
using System.Text.Json;

namespace UnifiedToolkit.RepositoryAssets;

public sealed class AssetRepositoryCatalogueBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AssetRepositoryCatalogueResult Build(string repositoryRoot, string? outputFolder = null)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));
        }

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository folder does not exist: {repositoryRoot}");
        }

        var assetsRoot = Path.Combine(repositoryRoot, "assets");
        var sourceRoot = Path.Combine(assetsRoot, "source");
        var generatedRoot = Path.Combine(assetsRoot, "generated");
        var manifestRoot = outputFolder is null
            ? Path.Combine(assetsRoot, "manifests")
            : Path.GetFullPath(outputFolder);

        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(generatedRoot);
        Directory.CreateDirectory(manifestRoot);

        var files = EnumerateFiles(sourceRoot)
            .Concat(EnumerateFiles(generatedRoot))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var records = new List<RepositoryAssetRecord>(files.Count);
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            var sha256 = ComputeSha256(file);
            var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, file));
            var origin = DetermineOrigin(relativePath);
            var kind = DetermineKind(file);

            records.Add(new RepositoryAssetRecord
            {
                AssetId = $"AST-{sha256[..16].ToUpperInvariant()}",
                RepositoryPath = relativePath,
                FileName = info.Name,
                Extension = info.Extension.ToLowerInvariant(),
                Kind = kind,
                Origin = origin,
                IsGenerated = relativePath.StartsWith("assets/generated/", StringComparison.OrdinalIgnoreCase),
                SizeBytes = info.Length,
                Sha256 = sha256,
                LastWriteTimeUtc = info.LastWriteTimeUtc
            });
        }

        var duplicateGroups = records
            .GroupBy(record => record.Sha256, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RepositoryAssetDuplicateGroup
            {
                Sha256 = group.Key,
                AssetId = group.First().AssetId,
                FileCount = group.Count(),
                RepositoryPaths = group.Select(item => item.RepositoryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .ToList();

        var catalogue = new RepositoryAssetCatalogue
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = NormalizePath(repositoryRoot),
            AssetsRoot = NormalizePath(Path.GetRelativePath(repositoryRoot, assetsRoot)),
            TotalFiles = records.Count,
            UniqueAssets = records.Select(record => record.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            DuplicateFiles = duplicateGroups.Sum(group => group.FileCount - 1),
            Assets = records
        };

        var hashes = records
            .OrderBy(record => record.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .Select(record => new RepositoryAssetHash
            {
                AssetId = record.AssetId,
                RepositoryPath = record.RepositoryPath,
                Sha256 = record.Sha256,
                SizeBytes = record.SizeBytes
            })
            .ToList();

        var provenance = records
            .OrderBy(record => record.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .Select(record => new RepositoryAssetProvenance
            {
                AssetId = record.AssetId,
                RepositoryPath = record.RepositoryPath,
                Origin = record.Origin,
                IsGenerated = record.IsGenerated,
                OriginalSource = null,
                Notes = record.IsGenerated
                    ? "Generated asset. Source linkage has not yet been recorded."
                    : "Imported source asset. Original source linkage has not yet been recorded."
            })
            .ToList();

        WriteJson(Path.Combine(manifestRoot, "assets.json"), catalogue);
        WriteJson(Path.Combine(manifestRoot, "hashes.json"), hashes);
        WriteJson(Path.Combine(manifestRoot, "provenance.json"), provenance);
        WriteJson(Path.Combine(manifestRoot, "duplicates.json"), duplicateGroups);
        WriteReport(Path.Combine(manifestRoot, "ASSET-CATALOGUE-REPORT.md"), catalogue, duplicateGroups);

        return new AssetRepositoryCatalogueResult
        {
            RepositoryRoot = repositoryRoot,
            AssetsRoot = assetsRoot,
            ManifestRoot = manifestRoot,
            TotalFiles = catalogue.TotalFiles,
            UniqueAssets = catalogue.UniqueAssets,
            DuplicateFiles = catalogue.DuplicateFiles,
            SourceFiles = records.Count(record => !record.IsGenerated),
            GeneratedFiles = records.Count(record => record.IsGenerated)
        };
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path));
    }

    private static bool IsIgnored(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals(".gitkeep", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string DetermineOrigin(string repositoryPath)
    {
        var segments = repositoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3 && segments[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[1].Equals("generated", StringComparison.OrdinalIgnoreCase))
            {
                return "generated";
            }

            if (segments[1].Equals("source", StringComparison.OrdinalIgnoreCase))
            {
                return segments[2].ToLowerInvariant();
            }
        }

        return "unknown";
    }

    private static string DetermineKind(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tga" => "image",
            ".obj" or ".fbx" or ".dae" or ".stl" or ".gltf" or ".glb" or ".mtl" => "model",
            ".ogg" or ".wav" or ".mp3" or ".flac" => "audio",
            ".lua" => "lua",
            ".json" => "json",
            ".pdf" => "pdf",
            ".xml" => "xml",
            ".txt" or ".md" => "text",
            ".zip" or ".7z" or ".rar" => "archive",
            _ => "other"
        };
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void WriteReport(
        string path,
        RepositoryAssetCatalogue catalogue,
        IReadOnlyCollection<RepositoryAssetDuplicateGroup> duplicateGroups)
    {
        var byOrigin = catalogue.Assets
            .GroupBy(asset => asset.Origin, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var byKind = catalogue.Assets
            .GroupBy(asset => asset.Kind, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        using var writer = new StreamWriter(path, false);
        writer.WriteLine("# Asset Catalogue Report");
        writer.WriteLine();
        writer.WriteLine($"Generated: {catalogue.GeneratedUtc:O}");
        writer.WriteLine();
        writer.WriteLine($"- Total files: {catalogue.TotalFiles}");
        writer.WriteLine($"- Unique assets: {catalogue.UniqueAssets}");
        writer.WriteLine($"- Duplicate files: {catalogue.DuplicateFiles}");
        writer.WriteLine($"- Duplicate hash groups: {duplicateGroups.Count}");
        writer.WriteLine();
        writer.WriteLine("## By origin");
        writer.WriteLine();
        foreach (var group in byOrigin)
        {
            writer.WriteLine($"- {group.Key}: {group.Count()}");
        }

        writer.WriteLine();
        writer.WriteLine("## By file kind");
        writer.WriteLine();
        foreach (var group in byKind)
        {
            writer.WriteLine($"- {group.Key}: {group.Count()}");
        }
    }
}

public sealed class AssetRepositoryCatalogueResult
{
    public string RepositoryRoot { get; init; } = string.Empty;
    public string AssetsRoot { get; init; } = string.Empty;
    public string ManifestRoot { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int UniqueAssets { get; init; }
    public int DuplicateFiles { get; init; }
    public int SourceFiles { get; init; }
    public int GeneratedFiles { get; init; }
}

public sealed class RepositoryAssetCatalogue
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string AssetsRoot { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int UniqueAssets { get; init; }
    public int DuplicateFiles { get; init; }
    public List<RepositoryAssetRecord> Assets { get; init; } = new();
}

public sealed class RepositoryAssetRecord
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public bool IsGenerated { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTime LastWriteTimeUtc { get; init; }
}

public sealed class RepositoryAssetHash
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

public sealed class RepositoryAssetProvenance
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public bool IsGenerated { get; init; }
    public string? OriginalSource { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed class RepositoryAssetDuplicateGroup
{
    public string Sha256 { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public List<string> RepositoryPaths { get; init; } = new();
}
