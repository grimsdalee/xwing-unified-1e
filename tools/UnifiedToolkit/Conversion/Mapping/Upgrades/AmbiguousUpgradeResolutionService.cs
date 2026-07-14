using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class AmbiguousUpgradeResolutionResult
{
    public int ResolutionCount { get; init; }
    public int MappingCount { get; init; }
    public int DispositionCount { get; init; }
    public int RemainingAmbiguousCount { get; init; }
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();
    public bool Applied { get; init; }
    public string BackupFolder { get; init; } = "";
}

public static class AmbiguousUpgradeResolutionService
{
    public static AmbiguousUpgradeResolutionResult Execute(
        string reviewPath,
        string mappingFolder,
        string targetVersion,
        bool apply)
    {
        var readOptions = CreateReadOptions();
        var reviews = JsonSerializer.Deserialize<List<AmbiguousUpgradeResolution>>(
            File.ReadAllText(reviewPath), readOptions) ?? new List<AmbiguousUpgradeResolution>();

        var upgradesPath = Path.Combine(mappingFolder, "upgrades.json");
        var alternatesPath = Path.Combine(mappingFolder, "upgrade-source-alternates.json");
        var dispositionsPath = Path.Combine(mappingFolder, "upgrade-dispositions.json");
        var manifestPath = Path.Combine(mappingFolder, "mapping-set.json");

        var upgrades = ReadList<UpgradeMapping>(upgradesPath, readOptions);
        var alternates = ReadList<UpgradeSourceAlternate>(alternatesPath, readOptions);
        var dispositions = ReadList<UpgradeDisposition>(dispositionsPath, readOptions);
        var ambiguous = dispositions
            .Where(x => x.Kind == UpgradeDispositionKind.Ambiguous)
            .ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);

        var issues = ValidateReviews(reviews, ambiguous);
        var addedMappings = new List<UpgradeMapping>();
        var replacementDispositions = new List<UpgradeDisposition>();

        if (issues.Count == 0)
        {
            foreach (var review in reviews)
            {
                if (review.Decision == AmbiguousUpgradeResolutionDecision.Map)
                {
                    var candidate = review.Candidates.Single(x =>
                        x.Id.Equals(review.SelectedTargetId, StringComparison.OrdinalIgnoreCase));

                    addedMappings.Add(new UpgradeMapping
                    {
                        MappingId = $"upgrade-{candidate.Id}-{candidate.Slot}-direct-v1".ToLowerInvariant(),
                        SourceId = review.SourceId,
                        TargetId = candidate.Id,
                        Name = candidate.Name,
                        Slot = candidate.Slot,
                        SquadPointCost = candidate.SquadPointCost,
                        Unique = candidate.Unique,
                        Factions = candidate.Factions.ToArray(),
                        ShipRestrictions = candidate.ShipRestrictions.ToArray(),
                        SizeRestrictions = candidate.SizeRestrictions.ToArray(),
                        Text = candidate.Text
                    });
                }
                else
                {
                    replacementDispositions.Add(new UpgradeDisposition
                    {
                        SourceId = review.SourceId,
                        Kind = review.Disposition!.Value,
                        Reason = review.Reason
                    });
                }
            }

            issues.AddRange(UpgradeMappingValidator.Validate(upgrades.Concat(addedMappings), alternates));
        }

        var remainingAmbiguous = ambiguous.Count - reviews.Count;
        if (!apply || issues.Count > 0)
        {
            return CreateResult(
                reviews.Count,
                addedMappings.Count,
                replacementDispositions.Count,
                remainingAmbiguous,
                issues,
                false,
                "");
        }

        var resolvedIds = reviews
            .Select(x => x.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updatedUpgrades = upgrades
            .Concat(addedMappings)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Slot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updatedDispositions = dispositions
            .Where(x => !resolvedIds.Contains(x.SourceId))
            .Concat(replacementDispositions)
            .OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var backupFolder = Path.Combine(
            mappingFolder,
            "backups",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupFolder);

        foreach (var path in new[] { upgradesPath, alternatesPath, dispositionsPath, manifestPath })
        {
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(backupFolder, Path.GetFileName(path)), true);
            }
        }

        var writeOptions = CreateWriteOptions();
        File.WriteAllText(upgradesPath, JsonSerializer.Serialize(updatedUpgrades, writeOptions) + Environment.NewLine);
        File.WriteAllText(dispositionsPath, JsonSerializer.Serialize(updatedDispositions, writeOptions) + Environment.NewLine);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { version = targetVersion }, writeOptions) + Environment.NewLine);

        return CreateResult(
            reviews.Count,
            addedMappings.Count,
            replacementDispositions.Count,
            remainingAmbiguous,
            issues,
            true,
            backupFolder);
    }

    private static List<string> ValidateReviews(
        IReadOnlyList<AmbiguousUpgradeResolution> reviews,
        IReadOnlyDictionary<string, UpgradeDisposition> ambiguous)
    {
        var issues = new List<string>();

        foreach (var duplicate in reviews
                     .GroupBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            issues.Add($"Duplicate resolution source ID: '{duplicate.Key}'.");
        }

        foreach (var review in reviews)
        {
            if (!ambiguous.ContainsKey(review.SourceId))
            {
                issues.Add($"Source upgrade '{review.SourceId}' is not currently ambiguous.");
            }

            if (review.Decision == AmbiguousUpgradeResolutionDecision.Unreviewed)
            {
                issues.Add($"Source upgrade '{review.SourceId}' is still unreviewed.");
            }

            if (string.IsNullOrWhiteSpace(review.Reason))
            {
                issues.Add($"Source upgrade '{review.SourceId}' requires a reason.");
            }

            if (review.Decision == AmbiguousUpgradeResolutionDecision.Map)
            {
                if (string.IsNullOrWhiteSpace(review.SelectedTargetId))
                {
                    issues.Add($"Mapped source upgrade '{review.SourceId}' requires selectedTargetId.");
                }

                var matches = review.Candidates.Count(x =>
                    x.Id.Equals(review.SelectedTargetId, StringComparison.OrdinalIgnoreCase));
                if (matches != 1)
                {
                    issues.Add($"Mapped source upgrade '{review.SourceId}' must select exactly one candidate; found {matches}.");
                }
            }

            if (review.Decision == AmbiguousUpgradeResolutionDecision.Disposition)
            {
                if (review.Disposition is null || review.Disposition == UpgradeDispositionKind.Ambiguous)
                {
                    issues.Add($"Disposition source upgrade '{review.SourceId}' requires a non-ambiguous disposition.");
                }
            }
        }

        foreach (var missing in ambiguous.Keys.Where(x =>
                     !reviews.Any(r => r.SourceId.Equals(x, StringComparison.OrdinalIgnoreCase))))
        {
            issues.Add($"Ambiguous source upgrade '{missing}' is missing from the review file.");
        }

        return issues;
    }

    private static List<T> ReadList<T>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), options) ?? new List<T>();
    }

    private static AmbiguousUpgradeResolutionResult CreateResult(
        int resolutionCount,
        int mappingCount,
        int dispositionCount,
        int remainingAmbiguousCount,
        IReadOnlyList<string> validationIssues,
        bool applied,
        string backupFolder) => new()
    {
        ResolutionCount = resolutionCount,
        MappingCount = mappingCount,
        DispositionCount = dispositionCount,
        RemainingAmbiguousCount = remainingAmbiguousCount,
        ValidationIssues = validationIssues,
        Applied = applied,
        BackupFolder = backupFolder
    };

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
