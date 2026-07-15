using UnifiedToolkit.Reports;
using System.Text.Json;

namespace UnifiedToolkit.Assets;

public sealed class ReviewedShipAssetApprovalResult
{
    public IReadOnlyList<AssetMappingRecord> NewMappings { get; init; } = Array.Empty<AssetMappingRecord>();
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();
    public int Unreviewed { get; init; }
}

public static class ReviewedShipAssetApprovalService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ReviewedShipAssetApprovalResult Build(
        ShipAssetReviewDocument document,
        AssetCatalogue catalogue,
        string assetsFolder)
    {
        var issues = new List<string>();
        var newMappings = new List<AssetMappingRecord>();
        var existingKeys = LoadExistingRoleKeys(assetsFolder);
        var assetsById = catalogue.Assets
            .GroupBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var unreviewed = 0;

        foreach (var entry in document.Entries)
        {
            if (entry.Decision.Equals("Unreviewed", StringComparison.OrdinalIgnoreCase))
            {
                unreviewed++;
                continue;
            }

            if (!entry.Decision.Equals("Approve", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Unsupported decision '{entry.Decision}' for {entry.Entity.SemanticKey} / {entry.Role}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                issues.Add($"Approval reason is required for {entry.Entity.SemanticKey} / {entry.Role}.");
                continue;
            }

            var roleKey = ShipAssetReviewService.RoleKey(entry.Entity.SemanticKey, entry.Role);
            if (existingKeys.Contains(roleKey))
            {
                issues.Add($"Role is already approved: {entry.Entity.SemanticKey} / {entry.Role}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.SelectedAssetId))
            {
                issues.Add($"SelectedAssetId is required for {entry.Entity.SemanticKey} / {entry.Role}.");
                continue;
            }

            var candidate = entry.Candidates.FirstOrDefault(x => x.AssetId.Equals(entry.SelectedAssetId, StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                issues.Add($"Selected asset is not a listed candidate for {entry.Entity.SemanticKey} / {entry.Role}: {entry.SelectedAssetId}.");
                continue;
            }

            if (!assetsById.TryGetValue(entry.SelectedAssetId, out var asset))
            {
                issues.Add($"Selected asset does not exist in catalogue: {entry.SelectedAssetId}.");
                continue;
            }

            if (!IsCompatible(entry.Role, asset.StructuralClass))
            {
                issues.Add($"Selected asset is structurally incompatible: {entry.Entity.SemanticKey} / {entry.Role} -> {asset.StructuralClass}.");
                continue;
            }

            newMappings.Add(new AssetMappingRecord
            {
                Entity = entry.Entity,
                EntityName = entry.EntityName,
                Role = entry.Role,
                AssetId = asset.AssetId,
                AssetName = asset.Name,
                AssetKind = asset.Kind,
                StructuralClass = asset.StructuralClass,
                SourceKind = asset.SourceKind,
                Location = string.IsNullOrWhiteSpace(asset.RelativePath) ? asset.SourcePath : asset.RelativePath,
                Score = candidate.Score,
                ConfidenceBand = candidate.ConfidenceBand,
                ApprovalKind = "Reviewed",
                Reason = entry.Reason
            });
        }

        ValidateDuplicateSelections(newMappings, issues);

        return new ReviewedShipAssetApprovalResult
        {
            NewMappings = newMappings.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role).ToArray(),
            ValidationIssues = issues.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
            Unreviewed = unreviewed
        };
    }

    public static void Apply(
        ReviewedShipAssetApprovalResult result,
        string assetsFolder,
        string targetVersion,
        string semanticVersion)
    {
        Directory.CreateDirectory(assetsFolder);
        var mappingsPath = Path.Combine(assetsFolder, "asset-mappings.json");
        var sharedPath = Path.Combine(assetsFolder, "shared-assets.json");
        var dispositionsPath = Path.Combine(assetsFolder, "asset-dispositions.json");

        var mappings = LoadList<AssetMappingRecord>(mappingsPath);
        var shared = LoadList<SharedAssetMappingRecord>(sharedPath);
        var dispositions = LoadList<AssetDispositionRecord>(dispositionsPath);

        mappings.AddRange(result.NewMappings.Where(x => x.Role != AssetRole.ShipBase));

        foreach (var mapping in result.NewMappings.Where(x => x.Role == AssetRole.ShipBase))
        {
            var existingShared = shared.FirstOrDefault(x =>
                x.AssetId.Equals(mapping.AssetId, StringComparison.OrdinalIgnoreCase) && x.Role == mapping.Role);
            if (existingShared is null)
            {
                shared.Add(new SharedAssetMappingRecord
                {
                    AssetId = mapping.AssetId,
                    AssetName = mapping.AssetName,
                    Role = mapping.Role,
                    AssetKind = mapping.AssetKind,
                    StructuralClass = mapping.StructuralClass,
                    SourceKind = mapping.SourceKind,
                    Location = mapping.Location,
                    SemanticKeys = new[] { mapping.Entity.SemanticKey },
                    Reason = mapping.Reason
                });
            }
            else
            {
                var replacement = new SharedAssetMappingRecord
                {
                    AssetId = existingShared.AssetId,
                    AssetName = existingShared.AssetName,
                    Role = existingShared.Role,
                    AssetKind = existingShared.AssetKind,
                    StructuralClass = existingShared.StructuralClass,
                    SourceKind = existingShared.SourceKind,
                    Location = existingShared.Location,
                    SemanticKeys = existingShared.SemanticKeys
                        .Append(mapping.Entity.SemanticKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToArray(),
                    Reason = existingShared.Reason
                };
                shared[shared.IndexOf(existingShared)] = replacement;
            }
        }

        var approvedKeys = result.NewMappings
            .Select(x => ShipAssetReviewService.RoleKey(x.Entity.SemanticKey, x.Role))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        dispositions = dispositions
            .Where(x => !approvedKeys.Contains(ShipAssetReviewService.RoleKey(x.Entity.SemanticKey, x.Role)))
            .ToList();

        var approvedAssignments = mappings.Count + shared.Sum(x => x.SemanticKeys.Count);
        var manifest = new AssetMappingSetManifest
        {
            AssetMappingVersion = targetVersion,
            SemanticMappingVersion = semanticVersion,
            ApprovedMappings = approvedAssignments,
            SharedAssignments = shared.Sum(x => x.SemanticKeys.Count),
            PendingReview = dispositions.Count(x => x.Status == "PendingReview"),
            OptionalNotRequired = dispositions.Count(x => x.Status == "OptionalNotRequired"),
            MissingRequired = dispositions.Count(x => x.Status == "MissingRequired")
        };

        AssetMappingReports.WriteJson(mappings.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role).ToArray(), mappingsPath);
        AssetMappingReports.WriteJson(shared.OrderBy(x => x.Role).ThenBy(x => x.AssetId).ToArray(), sharedPath);
        AssetMappingReports.WriteJson(dispositions.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role).ToArray(), dispositionsPath);
        AssetMappingReports.WriteJson(manifest, Path.Combine(assetsFolder, "asset-mapping-set.json"));
    }

    private static List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path)) return new List<T>();
        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? new List<T>();
    }

    private static HashSet<string> LoadExistingRoleKeys(string assetsFolder)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in LoadList<AssetMappingRecord>(Path.Combine(assetsFolder, "asset-mappings.json")))
            result.Add(ShipAssetReviewService.RoleKey(mapping.Entity.SemanticKey, mapping.Role));
        foreach (var shared in LoadList<SharedAssetMappingRecord>(Path.Combine(assetsFolder, "shared-assets.json")))
            foreach (var semanticKey in shared.SemanticKeys)
                result.Add(ShipAssetReviewService.RoleKey(semanticKey, shared.Role));
        return result;
    }

    private static void ValidateDuplicateSelections(IReadOnlyList<AssetMappingRecord> mappings, ICollection<string> issues)
    {
        foreach (var duplicate in mappings.GroupBy(x => ShipAssetReviewService.RoleKey(x.Entity.SemanticKey, x.Role)).Where(x => x.Count() > 1))
            issues.Add($"Duplicate reviewed role: {duplicate.Key}.");

        foreach (var duplicate in mappings.Where(x => x.Role != AssetRole.ShipBase).GroupBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            issues.Add($"Non-shareable asset selected for multiple reviewed roles: {duplicate.Key}.");
    }

    private static bool IsCompatible(AssetRole role, AssetStructuralClass structuralClass) => role switch
    {
        AssetRole.ShipModel => structuralClass == AssetStructuralClass.ShipModel,
        AssetRole.ShipTexture => structuralClass == AssetStructuralClass.ShipTexture,
        AssetRole.ShipBase => structuralClass is AssetStructuralClass.SharedSmallBase or AssetStructuralClass.SharedLargeBase or AssetStructuralClass.SharedHugeBase,
        AssetRole.ShipDial => structuralClass is AssetStructuralClass.DialBag or AssetStructuralClass.DialObject,
        _ => false
    };
}
