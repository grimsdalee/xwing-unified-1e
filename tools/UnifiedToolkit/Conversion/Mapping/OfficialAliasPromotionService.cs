using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping.Dispositions;

namespace UnifiedToolkit.Conversion.Mapping;

public static class OfficialAliasPromotionService
{
    public static OfficialAliasPromotionResult Preview(string proposedMappingsPath, string mappingFolder, string targetVersion)
    {
        var proposedPath = Path.GetFullPath(proposedMappingsPath);
        var folder = Path.GetFullPath(mappingFolder);
        if (!File.Exists(proposedPath)) throw new FileNotFoundException("Official alias proposals file not found.", proposedPath);

        var current = ConversionMappingLoader.Load(folder);
        var proposed = JsonSerializer.Deserialize<List<ShipMapping>>(File.ReadAllText(proposedPath), Options(false))
            ?? throw new InvalidDataException("Unable to deserialize official alias proposals.");

        var issues = new List<ConversionIssue>();
        var currentMappings = current.Ships.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        var dispositions = current.ShipDispositions.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in proposed)
        {
            if (currentMappings.ContainsKey(mapping.SourceId))
            {
                issues.Add(Issue("Error", "AliasAlreadyMapped", mapping.SourceId,
                    "A live ship mapping already exists for this source ship."));
            }

            if (!dispositions.TryGetValue(mapping.SourceId, out var disposition))
            {
                issues.Add(Issue("Error", "AliasMissingDisposition", mapping.SourceId,
                    "The source ship does not have a reviewed disposition to promote."));
            }
            else if (disposition.Kind != ShipDispositionKind.Deferred && disposition.Kind != ShipDispositionKind.Alias)
            {
                issues.Add(Issue("Error", "AliasDispositionNotPromotable", mapping.SourceId,
                    $"Disposition '{disposition.Kind}' cannot be promoted as an official alias."));
            }
        }

        var mergedMappings = current.Ships.Concat(proposed)
            .OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var promotedIds = proposed.Select(x => x.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingDispositions = current.ShipDispositions
            .Where(x => !promotedIds.Contains(x.SourceId))
            .OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidate = new ConversionMappingSet
        {
            Version = targetVersion,
            Ships = mergedMappings,
            ShipDispositions = remainingDispositions
        };
        issues.AddRange(ConversionMappingValidator.Validate(candidate));

        return new OfficialAliasPromotionResult
        {
            ProposedMappingsPath = proposedPath,
            MappingFolder = folder,
            CurrentVersion = current.Version,
            TargetVersion = targetVersion,
            ProposedCount = proposed.Count,
            MergedMappings = mergedMappings,
            RemainingDispositions = remainingDispositions,
            ValidationIssues = issues
        };
    }

    public static OfficialAliasPromotionResult Apply(string proposedMappingsPath, string mappingFolder, string targetVersion)
    {
        var result = Preview(proposedMappingsPath, mappingFolder, targetVersion);
        if (result.ValidationIssues.Any(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))) return result;

        var shipsPath = Path.Combine(result.MappingFolder, "ships.json");
        var dispositionsPath = Path.Combine(result.MappingFolder, "ship-dispositions.json");
        var manifestPath = Path.Combine(result.MappingFolder, "mapping-set.json");
        var backupFolder = Path.Combine(result.MappingFolder, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupFolder);
        File.Copy(shipsPath, Path.Combine(backupFolder, "ships.json"), true);
        if (File.Exists(dispositionsPath)) File.Copy(dispositionsPath, Path.Combine(backupFolder, "ship-dispositions.json"), true);
        File.Copy(manifestPath, Path.Combine(backupFolder, "mapping-set.json"), true);

        var options = Options(true);
        File.WriteAllText(shipsPath, JsonSerializer.Serialize(result.MergedMappings, options) + Environment.NewLine);
        File.WriteAllText(dispositionsPath, JsonSerializer.Serialize(result.RemainingDispositions, options) + Environment.NewLine);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new Manifest { Version = targetVersion }, options) + Environment.NewLine);
        result.Applied = true;
        result.BackupFolder = backupFolder;
        return result;
    }

    private static ConversionIssue Issue(string severity, string code, string sourceId, string message) => new()
    {
        Severity = severity,
        Category = "Mapping",
        Code = code,
        SourceType = "Ship",
        SourceId = sourceId,
        Message = message
    };

    private static JsonSerializerOptions Options(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class Manifest { public string Version { get; init; } = ""; }
}

public sealed class OfficialAliasPromotionResult
{
    public string ProposedMappingsPath { get; init; } = "";
    public string MappingFolder { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public int ProposedCount { get; init; }
    public IReadOnlyList<ShipMapping> MergedMappings { get; init; } = Array.Empty<ShipMapping>();
    public IReadOnlyList<ShipDisposition> RemainingDispositions { get; init; } = Array.Empty<ShipDisposition>();
    public IReadOnlyList<ConversionIssue> ValidationIssues { get; init; } = Array.Empty<ConversionIssue>();
    public bool Applied { get; set; }
    public string BackupFolder { get; set; } = "";
}
