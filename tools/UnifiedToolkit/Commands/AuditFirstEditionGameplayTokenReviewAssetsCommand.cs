using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands;

/// <summary>Read-only audit that protects review files referenced by canonical gameplay-object manifests.</summary>
public static class AuditFirstEditionGameplayTokenReviewAssetsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E Gameplay Token Review-Asset Cleanup Audit");
        Console.WriteLine("====================================================================");
        Console.WriteLine();

        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var reviewRoot = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review");
            var manifestRoot = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects");
            var output = Path.GetFullPath(Option(args, "--output") ?? Resolve(repository,
                "_unifiedtoolkit_reports/phase16/gameplay-token-review-cleanup"));

            RequireDirectory(repository, "Repository");
            RequireDirectory(reviewRoot, "Gameplay-token review folder");
            RequireDirectory(manifestRoot, "Gameplay-object manifest folder");

            var manifestPaths = Directory.EnumerateFiles(manifestRoot, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            var references = LoadReferences(repository, manifestPaths);
            var files = Directory.EnumerateFiles(reviewRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => InspectFile(repository, path, references)).ToList();
            var missing = references.Keys.Where(path => !File.Exists(Resolve(repository, path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new MissingReviewReference(path, references[path])).ToList();

            var report = new ReviewAssetCleanupAudit
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Read-only audit. Manifest-referenced promotion inputs are protected; no files are moved or deleted.",
                ReviewRoot = Relative(repository, reviewRoot),
                ManifestPaths = manifestPaths.Select(path => Relative(repository, path)).ToList(),
                ReviewFileCount = files.Count,
                ProtectedFileCount = files.Count(file => file.Status == "protected-manifest-reference"),
                CleanupCandidateCount = files.Count(file => file.Status == "unreferenced-cleanup-candidate"),
                MissingProtectedReferenceCount = missing.Count,
                Files = files,
                MissingReferences = missing
            };

            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "first-edition-gameplay-token-review-cleanup.json");
            var csvPath = Path.Combine(output, "first-edition-gameplay-token-review-cleanup.csv");
            var markdownPath = Path.Combine(output, "FIRST-EDITION-GAMEPLAY-TOKEN-REVIEW-CLEANUP.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, files);
            WriteMarkdown(markdownPath, report);

            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Gameplay-object manifests:     {manifestPaths.Count}");
            Console.WriteLine($"Review files:                  {report.ReviewFileCount}");
            Console.WriteLine($"Protected approved inputs:     {report.ProtectedFileCount}");
            Console.WriteLine($"Unreferenced cleanup candidates:{report.CleanupCandidateCount,3}");
            Console.WriteLine($"Missing protected references:  {report.MissingProtectedReferenceCount}");
            Console.WriteLine();
            Console.WriteLine($"Audit:  {jsonPath}");
            Console.WriteLine($"Files:  {csvPath}");
            Console.WriteLine($"Report: {markdownPath}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. No files were moved, deleted or modified.");
            return missing.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Gameplay-token review cleanup audit failed: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, List<string>> LoadReferences(string repository, IEnumerable<string> manifests)
    {
        var references = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            var root = JsonNode.Parse(File.ReadAllText(manifest));
            if (root is null)
                throw new InvalidDataException($"Could not parse manifest: {manifest}");
            CollectResolvedFrom(root, value =>
            {
                var relative = NormaliseRelative(value);
                if (!relative.StartsWith("assets/source/unified1e/gameplay-tokens/review/", StringComparison.OrdinalIgnoreCase))
                    return;
                if (!references.TryGetValue(relative, out var sources))
                {
                    sources = new List<string>();
                    references.Add(relative, sources);
                }
                var manifestRelative = Relative(repository, manifest);
                if (!sources.Contains(manifestRelative, StringComparer.OrdinalIgnoreCase))
                    sources.Add(manifestRelative);
            });
        }
        return references;
    }

    private static void CollectResolvedFrom(JsonNode node, Action<string> collect)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Key.Equals("ResolvedFrom", StringComparison.OrdinalIgnoreCase) &&
                    pair.Value is JsonValue value && value.TryGetValue<string>(out var path) && !string.IsNullOrWhiteSpace(path))
                    collect(path);
                if (pair.Value is not null)
                    CollectResolvedFrom(pair.Value, collect);
            }
            return;
        }
        if (node is JsonArray array)
            foreach (var child in array.Where(child => child is not null))
                CollectResolvedFrom(child!, collect);
    }

    private static ReviewAssetFile InspectFile(string repository, string path, IReadOnlyDictionary<string, List<string>> references)
    {
        var relative = Relative(repository, path);
        var protectedFile = references.TryGetValue(relative, out var manifests);
        return new ReviewAssetFile(
            relative,
            Path.GetExtension(path).ToLowerInvariant(),
            new FileInfo(path).Length,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            protectedFile ? "protected-manifest-reference" : "unreferenced-cleanup-candidate",
            manifests ?? new List<string>());
    }

    private static void WriteCsv(string path, IEnumerable<ReviewAssetFile> files)
    {
        var lines = new List<string> { "RepositoryPath,Extension,SizeBytes,Sha256,Status,ReferencedBy" };
        lines.AddRange(files.Select(file => string.Join(',',
            Quote(file.RepositoryPath), Quote(file.Extension), file.SizeBytes.ToString(), Quote(file.Sha256),
            Quote(file.Status), Quote(string.Join(';', file.ReferencedBy)))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteMarkdown(string path, ReviewAssetCleanupAudit report)
    {
        var lines = new List<string>
        {
            "# Phase 16E Gameplay Token Review-Asset Cleanup Audit", "",
            $"- Review files: **{report.ReviewFileCount}**",
            $"- Protected approved inputs: **{report.ProtectedFileCount}**",
            $"- Unreferenced cleanup candidates: **{report.CleanupCandidateCount}**",
            $"- Missing protected references: **{report.MissingProtectedReferenceCount}**", "",
            "No files were moved or deleted.", "", "## Unreferenced cleanup candidates", ""
        };
        var candidates = report.Files.Where(file => file.Status == "unreferenced-cleanup-candidate").ToList();
        lines.AddRange(candidates.Count == 0
            ? new[] { "- None" }
            : candidates.Select(file => $"- `{file.RepositoryPath}`"));
        lines.AddRange(new[] { "", "## Missing protected references", "" });
        lines.AddRange(report.MissingReferences.Count == 0
            ? new[] { "- None" }
            : report.MissingReferences.Select(item => $"- `{item.RepositoryPath}` referenced by {string.Join(", ", item.ReferencedBy.Select(path => $"`{path}`"))}"));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string NormaliseRelative(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static string Quote(object? value) => $"\"{(value?.ToString() ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void ShowUsage() => Console.WriteLine("Usage: audit-first-edition-gameplay-token-review-assets <first-edition-repo-folder> [--output <folder>]");
}

public sealed class ReviewAssetCleanupAudit
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Policy { get; init; } = "";
    public string ReviewRoot { get; init; } = "";
    public List<string> ManifestPaths { get; init; } = new();
    public int ReviewFileCount { get; init; }
    public int ProtectedFileCount { get; init; }
    public int CleanupCandidateCount { get; init; }
    public int MissingProtectedReferenceCount { get; init; }
    public List<ReviewAssetFile> Files { get; init; } = new();
    public List<MissingReviewReference> MissingReferences { get; init; } = new();
}

public sealed record ReviewAssetFile(string RepositoryPath, string Extension, long SizeBytes, string Sha256,
    string Status, List<string> ReferencedBy);
public sealed record MissingReviewReference(string RepositoryPath, List<string> ReferencedBy);
