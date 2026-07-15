using System.Text.Json;

namespace UnifiedToolkit.Assets;

public sealed class ShipAssetReviewDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public string SourceAssetMappingVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ShipAssetReviewEntry> Entries { get; init; } = Array.Empty<ShipAssetReviewEntry>();
}

public sealed class ShipAssetReviewEntry
{
    public AssetEntityKey Entity { get; init; } = new();
    public string EntityName { get; init; } = "";
    public AssetRole Role { get; init; }
    public string Decision { get; init; } = "Unreviewed";
    public string SelectedAssetId { get; init; } = "";
    public string Reason { get; init; } = "";
    public string RecommendedAssetId { get; init; } = "";
    public decimal? RecommendedScore { get; init; }
    public decimal? RunnerUpScore { get; init; }
    public IReadOnlyList<AssetResolutionCandidate> Candidates { get; init; } = Array.Empty<AssetResolutionCandidate>();
}

public sealed class ShipAssetReviewBuildResult
{
    public ShipAssetReviewDocument Document { get; init; } = new();
    public int AlreadyApproved { get; init; }
    public int PendingRoles => Document.Entries.Count;
    public int ClearRecommendations => Document.Entries.Count(x => !string.IsNullOrWhiteSpace(x.RecommendedAssetId));
}

public static class ShipAssetReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly AssetRole[] ShipRoles =
    {
        AssetRole.ShipModel,
        AssetRole.ShipTexture,
        AssetRole.ShipBase,
        AssetRole.ShipDial
    };

    public static ShipAssetReviewBuildResult Build(string fullReviewPath, string mappingFolder)
    {
        var reviews = AssetResolutionApproval.LoadReview(fullReviewPath);
        var assetsFolder = Path.Combine(mappingFolder, "assets");
        var approved = LoadApprovedRoleKeys(assetsFolder);
        var version = LoadAssetVersion(assetsFolder);

        var shipReviews = reviews
            .Where(x => x.Entity.EntityType.Equals("ship", StringComparison.OrdinalIgnoreCase))
            .Where(x => ShipRoles.Contains(x.Role))
            .Where(x => x.Required)
            .OrderBy(x => x.EntityName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Role)
            .ToArray();

        var entries = new List<ShipAssetReviewEntry>();
        var alreadyApproved = 0;

        foreach (var review in shipReviews)
        {
            var key = RoleKey(review.Entity.SemanticKey, review.Role);
            if (approved.Contains(key))
            {
                alreadyApproved++;
                continue;
            }

            var candidates = review.Candidates
                .Where(x => x.ConfidenceBand != AssetConfidenceBand.Rejected)
                .Take(10)
                .ToArray();
            var first = candidates.FirstOrDefault();
            var second = candidates.Skip(1).FirstOrDefault();
            var clear = first is not null &&
                first.ConfidenceBand == AssetConfidenceBand.AutoApprovable &&
                (second is null || first.Score - second.Score >= 0.08m);

            entries.Add(new ShipAssetReviewEntry
            {
                Entity = review.Entity,
                EntityName = review.EntityName,
                Role = review.Role,
                Decision = "Unreviewed",
                SelectedAssetId = "",
                Reason = "",
                RecommendedAssetId = clear ? first!.AssetId : "",
                RecommendedScore = first?.Score,
                RunnerUpScore = second?.Score,
                Candidates = candidates
            });
        }

        return new ShipAssetReviewBuildResult
        {
            AlreadyApproved = alreadyApproved,
            Document = new ShipAssetReviewDocument
            {
                SourceAssetMappingVersion = version,
                Entries = entries
            }
        };
    }

    public static ShipAssetReviewDocument LoadDocument(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Ship asset review file not found.", path);
        return JsonSerializer.Deserialize<ShipAssetReviewDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize ship asset review file.");
    }

    public static void WriteDocument(ShipAssetReviewDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static HashSet<string> LoadApprovedRoleKeys(string assetsFolder)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappingsPath = Path.Combine(assetsFolder, "asset-mappings.json");
        if (File.Exists(mappingsPath))
        {
            var mappings = JsonSerializer.Deserialize<List<AssetMappingRecord>>(File.ReadAllText(mappingsPath), JsonOptions) ?? new();
            foreach (var mapping in mappings) result.Add(RoleKey(mapping.Entity.SemanticKey, mapping.Role));
        }

        var sharedPath = Path.Combine(assetsFolder, "shared-assets.json");
        if (File.Exists(sharedPath))
        {
            var shared = JsonSerializer.Deserialize<List<SharedAssetMappingRecord>>(File.ReadAllText(sharedPath), JsonOptions) ?? new();
            foreach (var item in shared)
                foreach (var semanticKey in item.SemanticKeys)
                    result.Add(RoleKey(semanticKey, item.Role));
        }

        return result;
    }

    private static string LoadAssetVersion(string assetsFolder)
    {
        var path = Path.Combine(assetsFolder, "asset-mapping-set.json");
        if (!File.Exists(path)) return "none";
        var manifest = JsonSerializer.Deserialize<AssetMappingSetManifest>(File.ReadAllText(path), JsonOptions);
        return manifest?.AssetMappingVersion ?? "unknown";
    }

    internal static string RoleKey(string semanticKey, AssetRole role) => $"{semanticKey}|{role}";
}
