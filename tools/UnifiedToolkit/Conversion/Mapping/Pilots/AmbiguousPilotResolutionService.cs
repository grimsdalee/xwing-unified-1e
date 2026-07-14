using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class AmbiguousPilotResolutionResult
{
    public int ResolutionCount { get; init; }
    public int MappingCount { get; init; }
    public int DispositionCount { get; init; }
    public int RemainingAmbiguousCount { get; init; }
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();
    public bool Applied { get; init; }
    public string BackupFolder { get; init; } = "";
}

public static class AmbiguousPilotResolutionService
{
    public static AmbiguousPilotResolutionResult Execute(string reviewPath, string mappingFolder, string targetVersion, bool apply)
    {
        var options = ReadOptions();
        var reviews = JsonSerializer.Deserialize<List<AmbiguousPilotResolution>>(File.ReadAllText(reviewPath), options) ?? new();
        var pilotsPath = Path.Combine(mappingFolder, "pilots.json");
        var dispositionsPath = Path.Combine(mappingFolder, "pilot-dispositions.json");
        var manifestPath = Path.Combine(mappingFolder, "mapping-set.json");
        var pilots = JsonSerializer.Deserialize<List<PilotMapping>>(File.ReadAllText(pilotsPath), options) ?? new();
        var dispositions = JsonSerializer.Deserialize<List<PilotDisposition>>(File.ReadAllText(dispositionsPath), options) ?? new();
        var ambiguous = dispositions.Where(x => x.Kind == PilotDispositionKind.Ambiguous).ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        var issues = Validate(reviews, ambiguous);
        var addedMappings = new List<PilotMapping>();
        var replacementDispositions = new List<PilotDisposition>();

        if (issues.Count == 0)
        {
            foreach (var review in reviews)
            {
                if (review.Decision == AmbiguousPilotResolutionDecision.Map)
                {
                    var candidate = review.Candidates.Single(x => x.Id.Equals(review.SelectedTargetId, StringComparison.OrdinalIgnoreCase));
                    addedMappings.Add(new PilotMapping
                    {
                        MappingId = $"pilot-{candidate.Id}-{candidate.ShipId}-{candidate.Faction}-direct-v1".ToLowerInvariant(),
                        SourceId = review.SourceId,
                        TargetId = candidate.Id,
                        Name = candidate.Name,
                        ShipId = candidate.ShipId,
                        Faction = candidate.Faction,
                        PilotSkill = candidate.PilotSkill,
                        SquadPointCost = candidate.SquadPointCost,
                        Unique = candidate.Unique,
                        UpgradeSlots = candidate.UpgradeSlots.ToArray()
                    });
                }
                else
                {
                    replacementDispositions.Add(new PilotDisposition
                    {
                        SourceId = review.SourceId,
                        Kind = review.Disposition!.Value,
                        Reason = review.Reason
                    });
                }
            }
            issues.AddRange(PilotMappingValidator.Validate(pilots.Concat(addedMappings), Array.Empty<PilotSourceAlternate>()));
        }

        var remaining = ambiguous.Count - reviews.Count;
        if (!apply || issues.Count > 0)
            return Result(reviews, addedMappings, replacementDispositions, remaining, issues, false, "");

        var updatedPilots = pilots.Concat(addedMappings).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ShipId, StringComparer.OrdinalIgnoreCase).ToList();
        var resolvedIds = reviews.Select(x => x.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updatedDispositions = dispositions.Where(x => !resolvedIds.Contains(x.SourceId)).Concat(replacementDispositions).OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToList();
        var backup = Path.Combine(mappingFolder, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach (var file in new[] { pilotsPath, dispositionsPath, manifestPath })
            if (File.Exists(file)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);
        var write = WriteOptions();
        File.WriteAllText(pilotsPath, JsonSerializer.Serialize(updatedPilots, write) + Environment.NewLine);
        File.WriteAllText(dispositionsPath, JsonSerializer.Serialize(updatedDispositions, write) + Environment.NewLine);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { version = targetVersion }, write) + Environment.NewLine);
        return Result(reviews, addedMappings, replacementDispositions, remaining, issues, true, backup);
    }

    private static List<string> Validate(IReadOnlyList<AmbiguousPilotResolution> reviews, IReadOnlyDictionary<string, PilotDisposition> ambiguous)
    {
        var issues = new List<string>();
        foreach (var duplicate in reviews.GroupBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            issues.Add($"Duplicate resolution source ID: '{duplicate.Key}'.");
        foreach (var review in reviews)
        {
            if (!ambiguous.ContainsKey(review.SourceId)) issues.Add($"Source pilot '{review.SourceId}' is not currently ambiguous.");
            if (review.Decision == AmbiguousPilotResolutionDecision.Unreviewed) issues.Add($"Source pilot '{review.SourceId}' is still unreviewed.");
            if (string.IsNullOrWhiteSpace(review.Reason)) issues.Add($"Source pilot '{review.SourceId}' requires a reason.");
            if (review.Decision == AmbiguousPilotResolutionDecision.Map)
            {
                if (string.IsNullOrWhiteSpace(review.SelectedTargetId)) issues.Add($"Mapped source pilot '{review.SourceId}' requires selectedTargetId.");
                var matches = review.Candidates.Count(x => x.Id.Equals(review.SelectedTargetId, StringComparison.OrdinalIgnoreCase));
                if (matches != 1) issues.Add($"Mapped source pilot '{review.SourceId}' must select exactly one candidate; found {matches}.");
            }
            if (review.Decision == AmbiguousPilotResolutionDecision.Disposition)
            {
                if (review.Disposition is null || review.Disposition == PilotDispositionKind.Ambiguous)
                    issues.Add($"Disposition source pilot '{review.SourceId}' requires a non-ambiguous disposition.");
            }
        }
        foreach (var missing in ambiguous.Keys.Where(x => !reviews.Any(r => r.SourceId.Equals(x, StringComparison.OrdinalIgnoreCase))))
            issues.Add($"Ambiguous source pilot '{missing}' is missing from the review file.");
        return issues;
    }

    private static AmbiguousPilotResolutionResult Result(IReadOnlyList<AmbiguousPilotResolution> reviews, IReadOnlyList<PilotMapping> mappings, IReadOnlyList<PilotDisposition> dispositions, int remaining, IReadOnlyList<string> issues, bool applied, string backup) => new()
    {
        ResolutionCount = reviews.Count,
        MappingCount = mappings.Count,
        DispositionCount = dispositions.Count,
        RemainingAmbiguousCount = remaining,
        ValidationIssues = issues,
        Applied = applied,
        BackupFolder = backup
    };

    private static JsonSerializerOptions ReadOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    private static JsonSerializerOptions WriteOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
