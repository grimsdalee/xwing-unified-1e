using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Runtime;

public sealed class RuntimeAssetIngestionResult
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string PrototypePath { get; set; } = "";
    public string UnifiedRepositoryPath { get; set; } = "";
    public string FirstEditionRepositoryPath { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public string RewrittenPrototypePath { get; set; } = "";
    public int UrlOccurrences { get; set; }
    public int UniqueUrls { get; set; }
    public int LocalAssetsCopied { get; set; }
    public int ExistingAssetsVerified { get; set; }
    public int ExternalAssetsDownloaded { get; set; }
    public int ExternalAssetsDeferred { get; set; }
    public int RewrittenUrlOccurrences { get; set; }
    public bool ReadyForRepositoryOwnedClone { get; set; }
    public List<RuntimeAssetManifestEntry> Assets { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class RuntimeAssetManifestEntry
{
    public string SourceUrl { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string SourceLocalPath { get; set; } = "";
    public string RepositoryRelativePath { get; set; } = "";
    public string DestinationLocalPath { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public int Occurrences { get; set; }
    public string Status { get; set; } = "";
    public string Error { get; set; } = "";
}

public static class RuntimePrototypeAssetIngester
{
    private const string UnifiedRawPrefix = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/";
    private static readonly Regex UrlRegex = new(@"https?://[^\s""'<>\\]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static RuntimeAssetIngestionResult Ingest(
        string prototypePath,
        string unifiedRepositoryPath,
        string firstEditionRepositoryPath,
        string publicBaseUrl,
        bool downloadExternal,
        string outputFolder)
    {
        var result = new RuntimeAssetIngestionResult
        {
            PrototypePath = Path.GetFullPath(prototypePath),
            UnifiedRepositoryPath = Path.GetFullPath(unifiedRepositoryPath),
            FirstEditionRepositoryPath = Path.GetFullPath(firstEditionRepositoryPath),
            PublicBaseUrl = publicBaseUrl.TrimEnd('/')
        };

        var root = JsonNode.Parse(File.ReadAllText(prototypePath))
            ?? throw new InvalidOperationException("Could not parse the runtime prototype JSON.");

        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        CollectUrls(root, occurrences);
        result.UrlOccurrences = occurrences.Values.Sum();
        result.UniqueUrls = occurrences.Count;

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UnifiedToolkit/1.0");

        foreach (var pair in occurrences.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entry = ResolveAsset(pair.Key, pair.Value, result, client, downloadExternal);
            result.Assets.Add(entry);
            if (entry.Status is "Copied" or "Verified" or "Downloaded")
                replacements[pair.Key] = entry.RepositoryUrl;
        }

        result.RewrittenUrlOccurrences = RewriteUrls(root, replacements);
        Directory.CreateDirectory(outputFolder);
        var rewrittenPath = Path.Combine(outputFolder, "runtime-ship-prototype.repository-assets.json");
        File.WriteAllText(rewrittenPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        result.RewrittenPrototypePath = rewrittenPath;

        foreach (var asset in result.Assets)
        {
            switch (asset.Status)
            {
                case "Copied": result.LocalAssetsCopied++; break;
                case "Verified": result.ExistingAssetsVerified++; break;
                case "Downloaded": result.ExternalAssetsDownloaded++; break;
                case "Deferred": result.ExternalAssetsDeferred++; break;
            }
        }

        var unresolvedRequired = result.Assets.Count(x => x.SourceKind == "UnifiedRepository" && x.Status is not ("Copied" or "Verified"));
        if (unresolvedRequired > 0)
            result.ValidationErrors.Add($"{unresolvedRequired} Unified repository assets could not be mirrored.");

        result.ReadyForRepositoryOwnedClone = result.ValidationErrors.Count == 0;
        result.Notes.Add("All mirrored files are stored beneath assets/first-edition/mirrored so their provenance remains explicit.");
        result.Notes.Add("External assets are only downloaded when --download-external is supplied; otherwise they remain listed as deferred in the manifest.");
        result.Notes.Add("The rewritten prototype uses repository-owned public URLs and can be passed to clone-runtime-ship-prototype after the assets are committed and reachable.");
        return result;
    }

    private static RuntimeAssetManifestEntry ResolveAsset(
        string sourceUrl,
        int occurrences,
        RuntimeAssetIngestionResult result,
        HttpClient client,
        bool downloadExternal)
    {
        var entry = new RuntimeAssetManifestEntry { SourceUrl = sourceUrl, Occurrences = occurrences };
        try
        {
            if (sourceUrl.StartsWith(UnifiedRawPrefix, StringComparison.OrdinalIgnoreCase))
            {
                entry.SourceKind = "UnifiedRepository";
                var sourceRelative = WebUtility.UrlDecode(sourceUrl[UnifiedRawPrefix.Length..]).Replace('/', Path.DirectorySeparatorChar);
                var sourcePath = Path.Combine(result.UnifiedRepositoryPath, sourceRelative);
                var unifiedDestinationRelative = Path.Combine("assets", "first-edition", "mirrored", "unified-2.5", sourceRelative);
                PopulateLocalEntry(entry, sourcePath, unifiedDestinationRelative, result);
                return entry;
            }

            entry.SourceKind = IsSteamUrl(sourceUrl) ? "SteamWorkshop" : "External";
            var uri = new Uri(sourceUrl);
            var extension = SafeExtension(uri.AbsolutePath);
            var stableName = Sha256Text(sourceUrl)[..20].ToLowerInvariant() + extension;
            var destinationRelative = Path.Combine("assets", "first-edition", "mirrored", "external", entry.SourceKind.ToLowerInvariant(), stableName);
            var destinationPath = Path.Combine(result.FirstEditionRepositoryPath, destinationRelative);
            entry.RepositoryRelativePath = ForwardSlashes(destinationRelative);
            entry.DestinationLocalPath = destinationPath;
            entry.RepositoryUrl = result.PublicBaseUrl + "/" + entry.RepositoryRelativePath;

            if (File.Exists(destinationPath))
            {
                FillFileMetadata(entry, destinationPath);
                entry.Status = "Verified";
                return entry;
            }

            if (!downloadExternal)
            {
                entry.Status = "Deferred";
                return entry;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var bytes = client.GetByteArrayAsync(sourceUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(destinationPath, bytes);
            FillFileMetadata(entry, destinationPath);
            entry.Status = "Downloaded";
            return entry;
        }
        catch (Exception ex)
        {
            entry.Status = "Failed";
            entry.Error = ex.Message;
            return entry;
        }
    }

    private static void PopulateLocalEntry(RuntimeAssetManifestEntry entry, string sourcePath, string destinationRelative, RuntimeAssetIngestionResult result)
    {
        entry.SourceLocalPath = sourcePath;
        entry.RepositoryRelativePath = ForwardSlashes(destinationRelative);
        entry.DestinationLocalPath = Path.Combine(result.FirstEditionRepositoryPath, destinationRelative);
        entry.RepositoryUrl = result.PublicBaseUrl + "/" + entry.RepositoryRelativePath;

        if (!File.Exists(sourcePath))
        {
            entry.Status = "Failed";
            entry.Error = "Source file was not found in the Unified repository.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(entry.DestinationLocalPath)!);
        if (File.Exists(entry.DestinationLocalPath) && FilesMatch(sourcePath, entry.DestinationLocalPath))
        {
            FillFileMetadata(entry, entry.DestinationLocalPath);
            entry.Status = "Verified";
            return;
        }

        File.Copy(sourcePath, entry.DestinationLocalPath, overwrite: true);
        FillFileMetadata(entry, entry.DestinationLocalPath);
        entry.Status = "Copied";
    }

    private static void CollectUrls(JsonNode? node, Dictionary<string, int> occurrences)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj) CollectUrls(pair.Value, occurrences);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) CollectUrls(item, occurrences);
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            foreach (Match match in UrlRegex.Matches(text))
            {
                var url = TrimUrl(match.Value);
                occurrences[url] = occurrences.GetValueOrDefault(url) + 1;
            }
        }
    }

    private static int RewriteUrls(JsonNode? node, IReadOnlyDictionary<string, string> replacements)
    {
        var count = 0;
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToList())
            {
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var rewritten = text;
                    foreach (var pair in replacements)
                    {
                        var before = rewritten;
                        rewritten = rewritten.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
                        if (!ReferenceEquals(before, rewritten) && !string.Equals(before, rewritten, StringComparison.Ordinal))
                            count += CountOccurrences(before, pair.Key);
                    }
                    obj[key] = rewritten;
                }
                else count += RewriteUrls(obj[key], replacements);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var rewritten = text;
                    foreach (var pair in replacements)
                    {
                        var before = rewritten;
                        rewritten = rewritten.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
                        if (!string.Equals(before, rewritten, StringComparison.Ordinal)) count += CountOccurrences(before, pair.Key);
                    }
                    array[i] = rewritten;
                }
                else count += RewriteUrls(array[i], replacements);
            }
        }
        return count;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static bool IsSteamUrl(string url)
        => url.Contains("steamusercontent", StringComparison.OrdinalIgnoreCase)
           || url.Contains("steamcommunity", StringComparison.OrdinalIgnoreCase)
           || url.Contains("akamaihd.net/ugc", StringComparison.OrdinalIgnoreCase);

    private static string SafeExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8) return ".bin";
        return extension.ToLowerInvariant();
    }

    private static string TrimUrl(string url) => url.TrimEnd('.', ',', ';', ')', ']', '}');
    private static string ForwardSlashes(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
    private static string Sha256Text(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static bool FilesMatch(string first, string second) => Sha256File(first).Equals(Sha256File(second), StringComparison.OrdinalIgnoreCase);
    private static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void FillFileMetadata(RuntimeAssetManifestEntry entry, string path)
    {
        var info = new FileInfo(path);
        entry.SizeBytes = info.Length;
        entry.Sha256 = Sha256File(path);
    }
}
