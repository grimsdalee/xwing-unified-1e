using System.Text;
using System.Text.Json;
using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.KnowledgeBase;

public sealed class KnowledgeBaseBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> UnavailableImgurUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://i.imgur.com/7w2BdHW.png",
        "https://i.imgur.com/7w2BdHW.png",
        "https://i.imgur.com/aCxRouI.jpg",
        "https://i.imgur.com/hGzGwY3.jpg",
        "https://i.imgur.com/oeiISeR.png",
        "https://i.imgur.com/W5AuCVd.jpg",
        "https://i.imgur.com/X3luGQr.jpg"
    };

    public KnowledgeBaseBuildResult Build(string repositoryRoot, string? outputFolder = null, bool refreshCatalogue = true)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository folder does not exist: {repositoryRoot}");
        }

        var manifestsRoot = Path.Combine(repositoryRoot, "assets", "manifests");
        var cataloguePath = Path.Combine(manifestsRoot, "assets.json");
        if (refreshCatalogue || !File.Exists(cataloguePath))
        {
            new AssetRepositoryCatalogueBuilder().Build(repositoryRoot);
        }

        var catalogue = ReadRequired<RepositoryAssetCatalogue>(cataloguePath);
        var unifiedManifest = ReadOptional<UnifiedAssetImportManifest>(Path.Combine(manifestsRoot, "unified25-import.json"));
        var legacyManifest = ReadOptional<LegacyAssetImportManifest>(Path.Combine(manifestsRoot, "legacy1e-import.json"));

        var unifiedByPath = (unifiedManifest?.Entries ?? new List<UnifiedAssetImportEntry>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DestinationRepositoryPath))
            .GroupBy(entry => Normalize(entry.DestinationRepositoryPath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var legacyByPath = (legacyManifest?.Entries ?? new List<LegacyAssetImportEntry>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DestinationRepositoryPath))
            .GroupBy(entry => Normalize(entry.DestinationRepositoryPath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var assets = catalogue.Assets
            .OrderBy(asset => asset.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .Select(asset => BuildAsset(asset, unifiedByPath, legacyByPath))
            .ToList();

        var unavailableSources = BuildUnavailableSources(legacyManifest, repositoryRoot);
        var duplicateGroups = assets
            .GroupBy(asset => asset.Sha256, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new KnowledgeBaseDuplicateGroup
            {
                AssetId = group.First().AssetId,
                Sha256 = group.Key,
                RepositoryPaths = group.Select(asset => asset.RepositoryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderByDescending(group => group.RepositoryPaths.Count)
            .ThenBy(group => group.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var validation = BuildValidation(assets, unavailableSources);
        var statistics = BuildStatistics(assets, unavailableSources, duplicateGroups);

        var knowledgeBase = new UnifiedKnowledgeBase
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalize(repositoryRoot),
            Domains = new KnowledgeBaseDomains
            {
                Assets = assets,
                UnavailableSources = unavailableSources,
                DuplicateGroups = duplicateGroups
            },
            Statistics = statistics,
            Validation = validation
        };

        var outputRoot = outputFolder is null
            ? Path.Combine(repositoryRoot, "ukb")
            : Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(outputRoot);

        WriteJson(Path.Combine(outputRoot, "knowledge-base.json"), knowledgeBase);
        WriteJson(Path.Combine(outputRoot, "assets.json"), new KnowledgeBaseAssetDomain
        {
            SchemaVersion = knowledgeBase.SchemaVersion,
            GeneratedUtc = knowledgeBase.GeneratedUtc,
            Assets = assets,
            UnavailableSources = unavailableSources,
            DuplicateGroups = duplicateGroups
        });
        WriteJson(Path.Combine(outputRoot, "statistics.json"), statistics);
        WriteJson(Path.Combine(outputRoot, "validation.json"), validation);
        WriteReport(Path.Combine(outputRoot, "KNOWLEDGE-BASE-REPORT.md"), knowledgeBase);

        return new KnowledgeBaseBuildResult
        {
            OutputRoot = outputRoot,
            FileCount = assets.Count,
            UniqueAssetCount = assets.Select(asset => asset.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            UnavailableSourceCount = unavailableSources.Count,
            DuplicateFileCount = duplicateGroups.Sum(group => group.RepositoryPaths.Count - 1),
            ErrorCount = validation.Issues.Count(issue => issue.Severity == "error"),
            WarningCount = validation.Issues.Count(issue => issue.Severity == "warning")
        };
    }

    private static KnowledgeBaseAsset BuildAsset(
        RepositoryAssetRecord asset,
        IReadOnlyDictionary<string, UnifiedAssetImportEntry> unifiedByPath,
        IReadOnlyDictionary<string, List<LegacyAssetImportEntry>> legacyByPath)
    {
        var path = Normalize(asset.RepositoryPath);
        var sourceReferences = new List<KnowledgeBaseSourceReference>();

        if (unifiedByPath.TryGetValue(path, out var unified))
        {
            sourceReferences.Add(new KnowledgeBaseSourceReference
            {
                SourceSystem = "unified25",
                SourceLocation = unified.SourceRelativePath,
                ImportStatus = unified.Status,
                ImportedUtc = null
            });
        }

        if (legacyByPath.TryGetValue(path, out var legacyEntries))
        {
            foreach (var entry in legacyEntries.OrderBy(item => item.SourceUrl, StringComparer.OrdinalIgnoreCase))
            {
                sourceReferences.Add(new KnowledgeBaseSourceReference
                {
                    SourceSystem = "legacy1e",
                    SourceLocation = entry.SourceUrl,
                    ImportStatus = entry.Status,
                    JsonPaths = entry.JsonPaths
                });
            }
        }

        return new KnowledgeBaseAsset
        {
            AssetId = asset.AssetId,
            Sha256 = asset.Sha256,
            RepositoryPath = path,
            FileName = asset.FileName,
            Extension = asset.Extension,
            AssetType = asset.Kind,
            Warehouse = asset.Origin,
            IsGenerated = asset.IsGenerated,
            SizeBytes = asset.SizeBytes,
            Availability = "available",
            ReleaseRequired = null,
            SourceReferences = sourceReferences,
            ReferencedBy = new List<KnowledgeBaseEntityReference>()
        };
    }

    private static List<KnowledgeBaseUnavailableSource> BuildUnavailableSources(LegacyAssetImportManifest? manifest, string repositoryRoot)
    {
        if (manifest is null)
        {
            return new List<KnowledgeBaseUnavailableSource>();
        }

        return manifest.Entries
            .Where(entry => IsUnavailable(entry, repositoryRoot))
            .Select(entry => new KnowledgeBaseUnavailableSource
            {
                SourceId = BuildSourceId(entry.SourceUrl),
                SourceSystem = "legacy1e",
                SourceUrl = entry.SourceUrl,
                Host = entry.Host,
                AssetType = SingularKind(entry.Kind),
                Status = UnavailableImgurUrls.Contains(entry.SourceUrl) ? "unavailable" : "download-failed",
                Reason = UnavailableImgurUrls.Contains(entry.SourceUrl)
                    ? "Imgur returned inaccessible/placeholder content and the asset was manually confirmed unavailable."
                    : entry.Error ?? "The source could not be downloaded.",
                JsonPaths = entry.JsonPaths,
                ReleaseRequired = null
            })
            .GroupBy(item => item.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsUnavailable(LegacyAssetImportEntry entry, string repositoryRoot)
    {
        if (UnavailableImgurUrls.Contains(entry.SourceUrl))
        {
            return true;
        }

        if (entry.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.DestinationRepositoryPath))
        {
            var localPath = Path.Combine(repositoryRoot, entry.DestinationRepositoryPath.Replace('/', Path.DirectorySeparatorChar));
            return !File.Exists(localPath);
        }

        return false;
    }

    private static KnowledgeBaseStatistics BuildStatistics(
        IReadOnlyCollection<KnowledgeBaseAsset> assets,
        IReadOnlyCollection<KnowledgeBaseUnavailableSource> unavailable,
        IReadOnlyCollection<KnowledgeBaseDuplicateGroup> duplicates)
        => new()
        {
            FileCount = assets.Count,
            UniqueAssetCount = assets.Select(asset => asset.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalBytes = assets.Sum(asset => asset.SizeBytes),
            DuplicateFileCount = duplicates.Sum(group => group.RepositoryPaths.Count - 1),
            UnavailableSourceCount = unavailable.Count,
            ByWarehouse = assets.GroupBy(asset => asset.Warehouse, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            ByAssetType = assets.GroupBy(asset => asset.AssetType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };

    private static KnowledgeBaseValidation BuildValidation(
        IReadOnlyCollection<KnowledgeBaseAsset> assets,
        IReadOnlyCollection<KnowledgeBaseUnavailableSource> unavailable)
    {
        var issues = new List<KnowledgeBaseValidationIssue>();
        foreach (var source in unavailable)
        {
            issues.Add(new KnowledgeBaseValidationIssue
            {
                Severity = "warning",
                Code = source.Status == "unavailable" ? "UKB-ASSET-SOURCE-UNAVAILABLE" : "UKB-ASSET-DOWNLOAD-FAILED",
                SubjectId = source.SourceId,
                Message = source.Reason
            });
        }

        foreach (var asset in assets.Where(asset => string.IsNullOrWhiteSpace(asset.Sha256)))
        {
            issues.Add(new KnowledgeBaseValidationIssue
            {
                Severity = "error",
                Code = "UKB-ASSET-HASH-MISSING",
                SubjectId = asset.AssetId,
                Message = $"Asset has no SHA-256 hash: {asset.RepositoryPath}"
            });
        }

        return new KnowledgeBaseValidation
        {
            IsValid = issues.All(issue => issue.Severity != "error"),
            Issues = issues
        };
    }

    private static string BuildSourceId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"SRC-{Convert.ToHexString(hash)[..16]}";
    }

    private static string SingularKind(string value) => value.EndsWith('s') ? value[..^1] : value;

    private static T ReadRequired<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
           ?? throw new InvalidOperationException($"Could not parse required file: {path}");

    private static T? ReadOptional<T>(string path)
        => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static void WriteReport(string path, UnifiedKnowledgeBase ukb)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Unified Knowledge Base Report");
        writer.WriteLine();
        writer.WriteLine($"Generated: `{ukb.GeneratedUtc:O}`");
        writer.WriteLine();
        writer.WriteLine("## Assets domain");
        writer.WriteLine();
        writer.WriteLine($"- Files: **{ukb.Statistics.FileCount}**");
        writer.WriteLine($"- Unique content assets: **{ukb.Statistics.UniqueAssetCount}**");
        writer.WriteLine($"- Total bytes: **{ukb.Statistics.TotalBytes:N0}**");
        writer.WriteLine($"- Duplicate files: **{ukb.Statistics.DuplicateFileCount}**");
        writer.WriteLine($"- Unavailable source references: **{ukb.Statistics.UnavailableSourceCount}**");
        writer.WriteLine();
        writer.WriteLine("## By warehouse");
        writer.WriteLine();
        foreach (var item in ukb.Statistics.ByWarehouse.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteLine($"- {item.Key}: {item.Value}");
        }
        writer.WriteLine();
        writer.WriteLine("## By asset type");
        writer.WriteLine();
        foreach (var item in ukb.Statistics.ByAssetType.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteLine($"- {item.Key}: {item.Value}");
        }
        writer.WriteLine();
        writer.WriteLine("## Validation");
        writer.WriteLine();
        writer.WriteLine($"- Valid: **{ukb.Validation.IsValid}**");
        writer.WriteLine($"- Errors: **{ukb.Validation.Issues.Count(issue => issue.Severity == "error")}**");
        writer.WriteLine($"- Warnings: **{ukb.Validation.Issues.Count(issue => issue.Severity == "warning")}**");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}

public sealed class UnifiedKnowledgeBase
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public KnowledgeBaseDomains Domains { get; init; } = new();
    public KnowledgeBaseStatistics Statistics { get; init; } = new();
    public KnowledgeBaseValidation Validation { get; init; } = new();
}

public sealed class KnowledgeBaseDomains
{
    public List<KnowledgeBaseAsset> Assets { get; init; } = new();
    public List<KnowledgeBaseUnavailableSource> UnavailableSources { get; init; } = new();
    public List<KnowledgeBaseDuplicateGroup> DuplicateGroups { get; init; } = new();
}

public sealed class KnowledgeBaseAssetDomain
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public List<KnowledgeBaseAsset> Assets { get; init; } = new();
    public List<KnowledgeBaseUnavailableSource> UnavailableSources { get; init; } = new();
    public List<KnowledgeBaseDuplicateGroup> DuplicateGroups { get; init; } = new();
}

public sealed class KnowledgeBaseAsset
{
    public string AssetId { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
    public string Warehouse { get; init; } = string.Empty;
    public bool IsGenerated { get; init; }
    public long SizeBytes { get; init; }
    public string Availability { get; init; } = string.Empty;
    public bool? ReleaseRequired { get; init; }
    public List<KnowledgeBaseSourceReference> SourceReferences { get; init; } = new();
    public List<KnowledgeBaseEntityReference> ReferencedBy { get; init; } = new();
}

public sealed class KnowledgeBaseSourceReference
{
    public string SourceSystem { get; init; } = string.Empty;
    public string SourceLocation { get; init; } = string.Empty;
    public string ImportStatus { get; init; } = string.Empty;
    public DateTimeOffset? ImportedUtc { get; init; }
    public List<string> JsonPaths { get; init; } = new();
}

public sealed class KnowledgeBaseEntityReference
{
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public sealed class KnowledgeBaseUnavailableSource
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public List<string> JsonPaths { get; init; } = new();
    public bool? ReleaseRequired { get; init; }
}

public sealed class KnowledgeBaseDuplicateGroup
{
    public string AssetId { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<string> RepositoryPaths { get; init; } = new();
}

public sealed class KnowledgeBaseStatistics
{
    public int FileCount { get; init; }
    public int UniqueAssetCount { get; init; }
    public long TotalBytes { get; init; }
    public int DuplicateFileCount { get; init; }
    public int UnavailableSourceCount { get; init; }
    public Dictionary<string, int> ByWarehouse { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ByAssetType { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class KnowledgeBaseValidation
{
    public bool IsValid { get; init; }
    public List<KnowledgeBaseValidationIssue> Issues { get; init; } = new();
}

public sealed class KnowledgeBaseValidationIssue
{
    public string Severity { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class KnowledgeBaseBuildResult
{
    public string OutputRoot { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public int UniqueAssetCount { get; init; }
    public int DuplicateFileCount { get; init; }
    public int UnavailableSourceCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
}
