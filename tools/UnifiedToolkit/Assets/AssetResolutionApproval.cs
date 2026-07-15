using System.Text.Json;

namespace UnifiedToolkit.Assets;

public sealed class AssetResolutionApprovalResult
{
    public IReadOnlyList<AssetMappingRecord> Mappings { get; init; } = Array.Empty<AssetMappingRecord>();
    public IReadOnlyList<SharedAssetMappingRecord> SharedAssets { get; init; } = Array.Empty<SharedAssetMappingRecord>();
    public IReadOnlyList<AssetDispositionRecord> Dispositions { get; init; } = Array.Empty<AssetDispositionRecord>();
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();

    public int PendingReview => Dispositions.Count(x => x.Status == "PendingReview");
    public int OptionalNotRequired => Dispositions.Count(x => x.Status == "OptionalNotRequired");
    public int MissingRequired => Dispositions.Count(x => x.Status == "MissingRequired");
}

public static class AssetResolutionApproval
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<AssetResolutionReview> LoadReview(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Asset review file not found.", path);
        return JsonSerializer.Deserialize<List<AssetResolutionReview>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize asset review file.");
    }

    public static AssetCatalogue LoadCatalogue(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Asset catalogue file not found.", path);
        return JsonSerializer.Deserialize<AssetCatalogue>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize asset catalogue file.");
    }

    public static AssetResolutionApprovalResult Build(
        IReadOnlyList<AssetResolutionReview> reviews,
        AssetCatalogue catalogue)
    {
        var issues = new List<string>();
        var mappings = new List<AssetMappingRecord>();
        var dispositions = new List<AssetDispositionRecord>();
        var assetsById = catalogue.Assets
            .GroupBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var duplicateRoles = reviews
            .GroupBy(x => (x.Entity.SemanticKey.ToLowerInvariant(), x.Role))
            .Where(x => x.Count() > 1)
            .ToArray();
        foreach (var duplicate in duplicateRoles)
            issues.Add($"Duplicate review role: {duplicate.Key.Item1} / {duplicate.Key.Role}.");

        foreach (var review in reviews.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role))
        {
            var recommended = review.Candidates.FirstOrDefault();

            if (!review.Required && recommended is null)
            {
                dispositions.Add(ToDisposition(review, "OptionalNotRequired", "This role is explicitly optional and no compatible asset was found."));
                continue;
            }

            if (review.Required && recommended is null)
            {
                dispositions.Add(ToDisposition(review, "MissingRequired", "No structurally compatible asset candidate exists for this required role."));
                issues.Add($"Missing required asset role: {review.Entity.SemanticKey} / {review.Role}.");
                continue;
            }

            if (recommended is null)
            {
                dispositions.Add(ToDisposition(review, "PendingReview", "No recommendation is available."));
                continue;
            }

            if (recommended.ConfidenceBand != AssetConfidenceBand.AutoApprovable)
            {
                dispositions.Add(ToDisposition(
                    review,
                    "PendingReview",
                    "The highest-ranked candidate requires human review before approval.",
                    recommended));
                continue;
            }

            if (!assetsById.TryGetValue(recommended.AssetId, out var asset))
            {
                issues.Add($"Recommended asset does not exist in catalogue: {recommended.AssetId} for {review.Entity.SemanticKey} / {review.Role}.");
                continue;
            }

            if (!IsCompatible(review.Role, asset.StructuralClass))
            {
                issues.Add($"Structurally incompatible automatic mapping: {review.Entity.SemanticKey} / {review.Role} -> {asset.StructuralClass}.");
                continue;
            }

            mappings.Add(new AssetMappingRecord
            {
                Entity = review.Entity,
                EntityName = review.EntityName,
                Role = review.Role,
                AssetId = asset.AssetId,
                AssetName = asset.Name,
                AssetKind = asset.Kind,
                StructuralClass = asset.StructuralClass,
                SourceKind = asset.SourceKind,
                Location = string.IsNullOrWhiteSpace(asset.RelativePath) ? asset.SourcePath : asset.RelativePath,
                Score = recommended.Score,
                ConfidenceBand = recommended.ConfidenceBand,
                ApprovalKind = "Automatic",
                Reason = "Top-ranked structurally compatible candidate was classified as AutoApprovable."
            });
        }

        ValidateAssetReuse(mappings, issues);

        var sharedRoles = new[] { AssetRole.ShipBase, AssetRole.ShipTemplate };
        var shared = mappings
            .Where(x => sharedRoles.Contains(x.Role))
            .GroupBy(x => (x.AssetId.ToLowerInvariant(), x.Role))
            .Where(x => x.Count() > 1)
            .Select(group =>
            {
                var first = group.First();
                return new SharedAssetMappingRecord
                {
                    AssetId = first.AssetId,
                    AssetName = first.AssetName,
                    Role = first.Role,
                    AssetKind = first.AssetKind,
                    StructuralClass = first.StructuralClass,
                    SourceKind = first.SourceKind,
                    Location = first.Location,
                    SemanticKeys = group.Select(x => x.Entity.SemanticKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                    Reason = "This asset role is explicitly shareable and is assigned to multiple semantic entities."
                };
            })
            .OrderBy(x => x.Role)
            .ThenBy(x => x.AssetId)
            .ToArray();

        var sharedKeys = shared
            .SelectMany(x => x.SemanticKeys.Select(key => (key, x.Role, x.AssetId)))
            .ToHashSet();

        var individualMappings = mappings
            .Where(x => !sharedKeys.Contains((x.Entity.SemanticKey, x.Role, x.AssetId)))
            .OrderBy(x => x.Entity.SemanticKey)
            .ThenBy(x => x.Role)
            .ToArray();

        return new AssetResolutionApprovalResult
        {
            Mappings = individualMappings,
            SharedAssets = shared,
            Dispositions = dispositions.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role).ToArray(),
            ValidationIssues = issues.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray()
        };
    }

    private static AssetDispositionRecord ToDisposition(
        AssetResolutionReview review,
        string status,
        string reason,
        AssetResolutionCandidate? candidate = null) => new()
    {
        Entity = review.Entity,
        EntityName = review.EntityName,
        Role = review.Role,
        Required = review.Required,
        Status = status,
        Reason = reason,
        CandidateCount = review.Candidates.Count,
        RecommendedAssetId = candidate?.AssetId ?? "",
        RecommendedScore = candidate?.Score
    };

    private static void ValidateAssetReuse(IReadOnlyList<AssetMappingRecord> mappings, ICollection<string> issues)
    {
        var permittedSharedRoles = new[] { AssetRole.ShipBase, AssetRole.ShipTemplate };

        foreach (var group in mappings.GroupBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
        {
            var roles = group.Select(x => x.Role).Distinct().ToArray();
            if (roles.Length > 1)
            {
                issues.Add($"Asset {group.Key} is automatically assigned to incompatible role types: {string.Join(", ", roles)}.");
                continue;
            }

            if (!permittedSharedRoles.Contains(roles[0]))
                issues.Add($"Asset {group.Key} is automatically assigned to multiple entities for non-shareable role {roles[0]}.");
        }
    }

    private static bool IsCompatible(AssetRole role, AssetStructuralClass structuralClass) => role switch
    {
        AssetRole.ShipModel => structuralClass == AssetStructuralClass.ShipModel,
        AssetRole.ShipTexture => structuralClass == AssetStructuralClass.ShipTexture,
        AssetRole.ShipBase => structuralClass is AssetStructuralClass.SharedSmallBase or AssetStructuralClass.SharedLargeBase or AssetStructuralClass.SharedHugeBase,
        AssetRole.ShipDial => structuralClass is AssetStructuralClass.DialBag or AssetStructuralClass.DialObject,
        AssetRole.ShipTemplate => structuralClass == AssetStructuralClass.ShipObjectTemplate,
        AssetRole.PilotCard => structuralClass is AssetStructuralClass.PilotCardImage or AssetStructuralClass.CardObjectTemplate,
        AssetRole.UpgradeCard => structuralClass is AssetStructuralClass.UpgradeCardImage or AssetStructuralClass.CardObjectTemplate,
        _ => false
    };
}
