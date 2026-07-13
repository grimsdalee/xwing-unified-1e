using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.Mapping;

public static class ShipMappingApprovalService
{
    public static ShipMappingApprovalResult Preview(
        string proposedMappingsPath,
        string mappingFolder,
        string targetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedMappingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);

        var proposedPath = Path.GetFullPath(proposedMappingsPath);
        var fullMappingFolder = Path.GetFullPath(mappingFolder);

        if (!File.Exists(proposedPath))
            throw new FileNotFoundException("Proposed ship mappings file not found.", proposedPath);

        var current = ConversionMappingLoader.Load(fullMappingFolder);
        var proposed = LoadProposedMappings(proposedPath);
        var merged = Merge(current.Ships, proposed);
        var candidateSet = new ConversionMappingSet
        {
            Version = targetVersion,
            Ships = merged
        };

        var issues = ConversionMappingValidator.Validate(candidateSet);
        var currentSourceIds = current.Ships
            .Select(mapping => mapping.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ShipMappingApprovalResult
        {
            ProposedMappingsPath = proposedPath,
            MappingFolder = fullMappingFolder,
            CurrentVersion = current.Version,
            TargetVersion = targetVersion,
            ExistingCount = current.Ships.Count,
            ProposedCount = proposed.Count,
            AddedCount = proposed.Count(mapping => !currentSourceIds.Contains(mapping.SourceId)),
            UnchangedCount = proposed.Count(mapping => current.Ships.Any(existing => Equivalent(existing, mapping))),
            MergedMappings = merged,
            ValidationIssues = issues
        };
    }

    public static ShipMappingApprovalResult Apply(
        string proposedMappingsPath,
        string mappingFolder,
        string targetVersion)
    {
        var result = Preview(proposedMappingsPath, mappingFolder, targetVersion);
        if (result.ValidationIssues.Any(issue =>
                issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        var shipsPath = Path.Combine(result.MappingFolder, "ships.json");
        var manifestPath = Path.Combine(result.MappingFolder, "mapping-set.json");
        var backupFolder = Path.Combine(
            result.MappingFolder,
            "backups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

        Directory.CreateDirectory(backupFolder);
        File.Copy(shipsPath, Path.Combine(backupFolder, "ships.json"), overwrite: true);
        File.Copy(manifestPath, Path.Combine(backupFolder, "mapping-set.json"), overwrite: true);

        var options = CreateJsonOptions(writeIndented: true);
        File.WriteAllText(shipsPath, JsonSerializer.Serialize(result.MergedMappings, options) + Environment.NewLine);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(new MappingManifest { Version = targetVersion }, options) + Environment.NewLine);

        result.Applied = true;
        result.BackupFolder = backupFolder;
        return result;
    }

    private static List<ShipMapping> LoadProposedMappings(string path)
    {
        var proposed = JsonSerializer.Deserialize<List<ShipMapping>>(
            File.ReadAllText(path),
            CreateJsonOptions(writeIndented: false));

        return proposed ?? throw new InvalidDataException(
            $"Unable to deserialize proposed ship mappings: {path}");
    }

    private static List<ShipMapping> Merge(
        IReadOnlyCollection<ShipMapping> current,
        IReadOnlyCollection<ShipMapping> proposed)
    {
        var bySourceId = current.ToDictionary(
            mapping => mapping.SourceId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in proposed)
        {
            if (bySourceId.TryGetValue(mapping.SourceId, out var existing))
            {
                if (!Equivalent(existing, mapping))
                {
                    throw new InvalidDataException(
                        $"Proposed mapping for source ship '{mapping.SourceId}' conflicts with the existing mapping. " +
                        "Existing mappings are never overwritten automatically.");
                }

                continue;
            }

            bySourceId.Add(mapping.SourceId, mapping);
        }

        return bySourceId.Values
            .OrderBy(mapping => mapping.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Equivalent(ShipMapping left, ShipMapping right)
    {
        return left.MappingId.Equals(right.MappingId, StringComparison.OrdinalIgnoreCase)
            && left.SourceId.Equals(right.SourceId, StringComparison.OrdinalIgnoreCase)
            && left.TargetId.Equals(right.TargetId, StringComparison.OrdinalIgnoreCase)
            && left.Kind == right.Kind
            && left.Name.Equals(right.Name, StringComparison.Ordinal)
            && left.Size.Equals(right.Size, StringComparison.OrdinalIgnoreCase)
            && left.Attack == right.Attack
            && left.Agility == right.Agility
            && left.Hull == right.Hull
            && left.Shields == right.Shields
            && left.ExclusionReason.Equals(right.ExclusionReason, StringComparison.Ordinal)
            && left.Actions.SequenceEqual(right.Actions, StringComparer.OrdinalIgnoreCase)
            && left.Factions.SequenceEqual(right.Factions, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions CreateJsonOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class MappingManifest
    {
        public string Version { get; init; } = "";
    }
}

public sealed class ShipMappingApprovalResult
{
    public string ProposedMappingsPath { get; init; } = "";
    public string MappingFolder { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public int ExistingCount { get; init; }
    public int ProposedCount { get; init; }
    public int AddedCount { get; init; }
    public int UnchangedCount { get; init; }
    public IReadOnlyList<ShipMapping> MergedMappings { get; init; } = Array.Empty<ShipMapping>();
    public IReadOnlyList<ConversionIssue> ValidationIssues { get; init; } = Array.Empty<ConversionIssue>();
    public bool Applied { get; set; }
    public string BackupFolder { get; set; } = "";
}
