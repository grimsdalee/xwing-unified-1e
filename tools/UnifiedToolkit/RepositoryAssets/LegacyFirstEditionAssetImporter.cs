using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.RepositoryAssets;

public sealed class LegacyFirstEditionAssetImporter
{
    private static readonly Regex UrlRegex = new(
        "https?://[^\\s\"'<>\\\\]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Dictionary<string, string> ContentTypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
        ["image/bmp"] = ".bmp",
        ["image/svg+xml"] = ".svg",
        ["image/tga"] = ".tga",
        ["model/obj"] = ".obj",
        ["model/mtl"] = ".mtl",
        ["model/gltf+json"] = ".gltf",
        ["model/gltf-binary"] = ".glb",
        ["audio/ogg"] = ".ogg",
        ["audio/mpeg"] = ".mp3",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["application/json"] = ".json",
        ["text/plain"] = ".txt",
        ["application/pdf"] = ".pdf"
    };

    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tga", ".dds", ".svg",
        ".obj", ".mtl", ".fbx", ".dae", ".stl", ".gltf", ".glb", ".3ds", ".blend",
        ".ogg", ".wav", ".mp3", ".flac", ".m4a",
        ".lua", ".json", ".xml", ".yaml", ".yml", ".csv", ".pdf", ".txt", ".md",
        ".zip", ".7z", ".rar", ".assetbundle", ".unity3d"
    };

    private readonly HttpClient _httpClient;

    public LegacyFirstEditionAssetImporter()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UnifiedToolkit", "1.0"));
    }

    public async Task<LegacyAssetImportResult> ImportAsync(
        string legacySavePath,
        string firstEditionRepositoryRoot,
        string? outputFolder,
        bool dryRun)
    {
        ValidateInputs(legacySavePath, firstEditionRepositoryRoot);

        legacySavePath = Path.GetFullPath(legacySavePath);
        firstEditionRepositoryRoot = Path.GetFullPath(firstEditionRepositoryRoot);

        var destinationRoot = Path.Combine(firstEditionRepositoryRoot, "assets", "source", "legacy1e");
        var manifestRoot = outputFolder is null
            ? Path.Combine(firstEditionRepositoryRoot, "assets", "manifests")
            : Path.GetFullPath(outputFolder);

        Directory.CreateDirectory(manifestRoot);
        if (!dryRun)
        {
            Directory.CreateDirectory(destinationRoot);
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(legacySavePath));
        var references = new List<LegacyAssetReference>();
        CollectReferences(document.RootElement, "$", references);

        var grouped = references
            .GroupBy(reference => CanonicalizeUrl(reference.Url), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var previousEntries = LoadPreviousEntries(Path.Combine(manifestRoot, "legacy1e-import.json"));
        var contentHashPaths = BuildExistingContentIndex(previousEntries, firstEditionRepositoryRoot);
        var entries = new List<LegacyAssetImportEntry>(grouped.Count);

        foreach (var group in grouped)
        {
            var url = group.Key;
            var paths = group.Select(reference => reference.JsonPath).Distinct(StringComparer.Ordinal).OrderBy(path => path).ToList();
            var entry = dryRun
                ? ProcessDryRun(url, paths, previousEntries, firstEditionRepositoryRoot)
                : await DownloadAsync(url, paths, destinationRoot, firstEditionRepositoryRoot, previousEntries, contentHashPaths);
            entries.Add(entry);
        }

        var manifest = new LegacyAssetImportManifest
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            DryRun = dryRun,
            LegacySavePath = NormalizePath(legacySavePath),
            FirstEditionRepositoryRoot = NormalizePath(firstEditionRepositoryRoot),
            DestinationRoot = NormalizePath(destinationRoot),
            ReferenceCount = references.Count,
            UniqueUrlCount = grouped.Count,
            Entries = entries
        };

        var manifestPath = Path.Combine(manifestRoot, dryRun ? "legacy1e-import.dry-run.json" : "legacy1e-import.json");
        var reportPath = Path.Combine(manifestRoot, dryRun ? "LEGACY1E-ASSET-IMPORT-DRY-RUN.md" : "LEGACY1E-ASSET-IMPORT-REPORT.md");
        await WriteJsonAsync(manifestPath, manifest);
        await File.WriteAllTextAsync(reportPath, BuildReport(manifest), new UTF8Encoding(false));

        AssetRepositoryCatalogueResult? catalogue = null;
        if (!dryRun)
        {
            catalogue = new AssetRepositoryCatalogueBuilder().Build(firstEditionRepositoryRoot);
        }

        return BuildResult(manifest, manifestPath, reportPath, catalogue?.ManifestRoot);
    }

    private static LegacyAssetImportEntry ProcessDryRun(
        string url,
        List<string> jsonPaths,
        IReadOnlyDictionary<string, LegacyAssetImportEntry> previousEntries,
        string repositoryRoot)
    {
        if (previousEntries.TryGetValue(url, out var previous)
            && !string.IsNullOrWhiteSpace(previous.DestinationRepositoryPath)
            && File.Exists(Path.Combine(repositoryRoot, previous.DestinationRepositoryPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            return previous with { JsonPaths = jsonPaths, Status = "unchanged", Error = null };
        }

        return new LegacyAssetImportEntry
        {
            SourceUrl = url,
            Host = GetHost(url),
            Kind = DetermineKindFromUrl(url),
            JsonPaths = jsonPaths,
            Status = "would-download"
        };
    }

    private async Task<LegacyAssetImportEntry> DownloadAsync(
        string url,
        List<string> jsonPaths,
        string destinationRoot,
        string repositoryRoot,
        IReadOnlyDictionary<string, LegacyAssetImportEntry> previousEntries,
        Dictionary<string, string> contentHashPaths)
    {
        try
        {
            if (previousEntries.TryGetValue(url, out var previous)
                && !string.IsNullOrWhiteSpace(previous.DestinationRepositoryPath))
            {
                var existingPath = Path.Combine(repositoryRoot, previous.DestinationRepositoryPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(existingPath))
                {
                    var existingHash = ComputeSha256(existingPath);
                    if (string.IsNullOrWhiteSpace(previous.Sha256)
                        || existingHash.Equals(previous.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        contentHashPaths.TryAdd(existingHash, existingPath);
                        return previous with
                        {
                            JsonPaths = jsonPaths,
                            SizeBytes = new FileInfo(existingPath).Length,
                            Sha256 = existingHash,
                            Status = "unchanged",
                            Error = null
                        };
                    }
                }
            }

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(url, jsonPaths, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var extension = DetermineExtension(url, contentType);
            var kind = DetermineKind(extension, contentType);
            var host = GetHost(url);
            var urlHash = ComputeSha256(Encoding.UTF8.GetBytes(url));
            var safeName = BuildSafeName(url, urlHash, extension);
            var relativePath = NormalizePath(Path.Combine("assets", "source", "legacy1e", SanitizeSegment(host), kind, safeName));
            var destinationPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var temporaryPath = destinationPath + ".download";

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(temporaryPath))
            {
                await source.CopyToAsync(target);
            }

            var hash = ComputeSha256(temporaryPath);
            var size = new FileInfo(temporaryPath).Length;

            if (contentHashPaths.TryGetValue(hash, out var duplicatePath) && File.Exists(duplicatePath))
            {
                File.Delete(temporaryPath);
                return new LegacyAssetImportEntry
                {
                    AssetId = $"AST-{hash[..16].ToUpperInvariant()}",
                    SourceUrl = url,
                    Host = host,
                    Kind = kind,
                    Extension = extension,
                    ContentType = contentType,
                    DestinationRepositoryPath = NormalizePath(Path.GetRelativePath(repositoryRoot, duplicatePath)),
                    SizeBytes = size,
                    Sha256 = hash,
                    JsonPaths = jsonPaths,
                    Status = "duplicate-content"
                };
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            contentHashPaths[hash] = destinationPath;

            return new LegacyAssetImportEntry
            {
                AssetId = $"AST-{hash[..16].ToUpperInvariant()}",
                SourceUrl = url,
                Host = host,
                Kind = kind,
                Extension = extension,
                ContentType = contentType,
                DestinationRepositoryPath = relativePath,
                SizeBytes = size,
                Sha256 = hash,
                JsonPaths = jsonPaths,
                Status = "downloaded"
            };
        }
        catch (Exception exception)
        {
            return Failure(url, jsonPaths, exception.Message);
        }
    }

    private static void CollectReferences(JsonElement element, string path, List<LegacyAssetReference> references)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectReferences(property.Value, $"{path}.{EscapePathSegment(property.Name)}", references);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectReferences(item, $"{path}[{index}]", references);
                    index++;
                }
                break;

            case JsonValueKind.String:
                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                foreach (Match match in UrlRegex.Matches(value))
                {
                    var cleaned = TrimUrlPunctuation(match.Value);
                    if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
                        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    {
                        references.Add(new LegacyAssetReference(cleaned, path));
                    }
                }
                break;
        }
    }

    private static string CanonicalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }

    private static string TrimUrlPunctuation(string value)
        => value.TrimEnd('.', ',', ';', ':', ')', ']', '}');

    private static string EscapePathSegment(string value)
        => value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-')
            ? value
            : $"['{value.Replace("'", "\\'")}']";

    private static string DetermineExtension(string url, string? contentType)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(Uri.UnescapeDataString(uri.AbsolutePath));
            if (KnownExtensions.Contains(extension))
            {
                return extension.ToLowerInvariant();
            }
        }

        if (!string.IsNullOrWhiteSpace(contentType)
            && ContentTypeExtensions.TryGetValue(contentType, out var mapped))
        {
            return mapped;
        }

        return ".bin";
    }

    private static string DetermineKindFromUrl(string url)
    {
        var extension = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetExtension(uri.AbsolutePath)
            : string.Empty;
        return DetermineKind(extension, null);
    }

    private static string DetermineKind(string? extension, string? contentType)
    {
        extension = extension?.ToLowerInvariant() ?? string.Empty;
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tga" or ".dds" or ".svg"
            || contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "images";
        }

        if (extension is ".obj" or ".mtl" or ".fbx" or ".dae" or ".stl" or ".gltf" or ".glb" or ".3ds" or ".blend" or ".assetbundle" or ".unity3d"
            || contentType?.StartsWith("model/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "models";
        }

        if (extension is ".ogg" or ".wav" or ".mp3" or ".flac" or ".m4a"
            || contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "audio";
        }

        if (extension is ".lua") return "lua";
        if (extension is ".json" or ".xml" or ".yaml" or ".yml" or ".csv") return "data";
        if (extension is ".pdf" or ".txt" or ".md") return "documents";
        if (extension is ".zip" or ".7z" or ".rar") return "archives";
        return "other";
    }

    private static string BuildSafeName(string url, string urlHash, string extension)
    {
        var stem = "asset";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var candidate = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                stem = SanitizeSegment(candidate);
            }
        }

        if (stem.Length > 80)
        {
            stem = stem[..80];
        }

        return $"{stem}__{urlHash[..16]}{extension}";
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static string GetHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "unknown-host";

    private static LegacyAssetImportEntry Failure(string url, List<string> paths, string error)
        => new()
        {
            SourceUrl = url,
            Host = GetHost(url),
            Kind = DetermineKindFromUrl(url),
            JsonPaths = paths,
            Status = "failed",
            Error = error
        };

    private static IReadOnlyDictionary<string, LegacyAssetImportEntry> LoadPreviousEntries(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new Dictionary<string, LegacyAssetImportEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<LegacyAssetImportManifest>(File.ReadAllText(manifestPath), JsonOptions);
            return manifest?.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.SourceUrl))
                .GroupBy(entry => entry.SourceUrl, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, LegacyAssetImportEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, LegacyAssetImportEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> BuildExistingContentIndex(
        IReadOnlyDictionary<string, LegacyAssetImportEntry> previousEntries,
        string repositoryRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in previousEntries.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.Sha256) || string.IsNullOrWhiteSpace(entry.DestinationRepositoryPath))
            {
                continue;
            }

            var path = Path.Combine(repositoryRoot, entry.DestinationRepositoryPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                result.TryAdd(entry.Sha256, path);
            }
        }

        return result;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidateInputs(string savePath, string repositoryRoot)
    {
        if (!File.Exists(savePath))
        {
            throw new FileNotFoundException("Legacy First Edition save file does not exist.", Path.GetFullPath(savePath));
        }

        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"First Edition repository folder does not exist: {Path.GetFullPath(repositoryRoot)}");
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
    }

    private static string BuildReport(LegacyAssetImportManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine(manifest.DryRun ? "# Legacy First Edition Asset Import — Dry Run" : "# Legacy First Edition Asset Import Report");
        builder.AppendLine();
        builder.AppendLine($"- Generated: `{manifest.GeneratedUtc:O}`");
        builder.AppendLine($"- Save: `{manifest.LegacySavePath}`");
        builder.AppendLine($"- URL references: **{manifest.ReferenceCount}**");
        builder.AppendLine($"- Unique URLs: **{manifest.UniqueUrlCount}**");
        builder.AppendLine($"- Downloaded: **{manifest.Entries.Count(entry => entry.Status == "downloaded")}**");
        builder.AppendLine($"- Unchanged: **{manifest.Entries.Count(entry => entry.Status == "unchanged")}**");
        builder.AppendLine($"- Duplicate content: **{manifest.Entries.Count(entry => entry.Status == "duplicate-content")}**");
        builder.AppendLine($"- Would download: **{manifest.Entries.Count(entry => entry.Status == "would-download")}**");
        builder.AppendLine($"- Failed: **{manifest.Entries.Count(entry => entry.Status == "failed")}**");
        builder.AppendLine();
        builder.AppendLine("## Status by host");
        builder.AppendLine();
        builder.AppendLine("| Host | URLs | Downloaded | Unchanged | Duplicates | Failed |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var hostGroup in manifest.Entries.GroupBy(entry => entry.Host).OrderBy(group => group.Key))
        {
            builder.AppendLine($"| {hostGroup.Key} | {hostGroup.Count()} | {hostGroup.Count(e => e.Status == "downloaded")} | {hostGroup.Count(e => e.Status == "unchanged")} | {hostGroup.Count(e => e.Status == "duplicate-content")} | {hostGroup.Count(e => e.Status == "failed")} |");
        }

        var failures = manifest.Entries.Where(entry => entry.Status == "failed").ToList();
        if (failures.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failures");
            builder.AppendLine();
            foreach (var failure in failures)
            {
                builder.AppendLine($"- `{failure.SourceUrl}` — {failure.Error}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Preservation and release-size policy");
        builder.AppendLine();
        builder.AppendLine("The imported files are source assets. The future asset/model register will identify the subset required by the First Edition mod. Release generation will include only referenced assets; unused source assets will not be shipped.");
        return builder.ToString();
    }

    private static LegacyAssetImportResult BuildResult(
        LegacyAssetImportManifest manifest,
        string manifestPath,
        string reportPath,
        string? catalogueRoot)
        => new()
        {
            ReferenceCount = manifest.ReferenceCount,
            UniqueUrlCount = manifest.UniqueUrlCount,
            DownloadedCount = manifest.Entries.Count(entry => entry.Status is "downloaded" or "would-download"),
            UnchangedCount = manifest.Entries.Count(entry => entry.Status == "unchanged"),
            DuplicateCount = manifest.Entries.Count(entry => entry.Status == "duplicate-content"),
            SkippedCount = manifest.Entries.Count(entry => entry.Status == "skipped"),
            FailedCount = manifest.Entries.Count(entry => entry.Status == "failed"),
            BytesDownloaded = manifest.Entries.Where(entry => entry.Status == "downloaded").Sum(entry => entry.SizeBytes ?? 0),
            DestinationRoot = manifest.DestinationRoot,
            ManifestPath = NormalizePath(manifestPath),
            ReportPath = NormalizePath(reportPath),
            CatalogueManifestRoot = catalogueRoot is null ? null : NormalizePath(catalogueRoot)
        };

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed record LegacyAssetReference(string Url, string JsonPath);

public sealed record LegacyAssetImportEntry
{
    public string? AssetId { get; init; }
    public string SourceUrl { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Kind { get; init; } = "other";
    public string? Extension { get; init; }
    public string? ContentType { get; init; }
    public string? DestinationRepositoryPath { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public List<string> JsonPaths { get; init; } = new();
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public sealed class LegacyAssetImportManifest
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public bool DryRun { get; init; }
    public string LegacySavePath { get; init; } = string.Empty;
    public string FirstEditionRepositoryRoot { get; init; } = string.Empty;
    public string DestinationRoot { get; init; } = string.Empty;
    public int ReferenceCount { get; init; }
    public int UniqueUrlCount { get; init; }
    public List<LegacyAssetImportEntry> Entries { get; init; } = new();
}

public sealed class LegacyAssetImportResult
{
    public int ReferenceCount { get; init; }
    public int UniqueUrlCount { get; init; }
    public int DownloadedCount { get; init; }
    public int UnchangedCount { get; init; }
    public int DuplicateCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public long BytesDownloaded { get; init; }
    public string DestinationRoot { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string? CatalogueManifestRoot { get; init; }
}
