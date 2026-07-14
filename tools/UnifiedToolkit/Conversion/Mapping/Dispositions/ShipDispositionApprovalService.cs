using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.Mapping.Dispositions;

public static class ShipDispositionApprovalService
{
    public static ShipDispositionApprovalResult Preview(string reviewPath, string mappingFolder, string targetVersion)
    {
        var path = Path.GetFullPath(reviewPath);
        var folder = Path.GetFullPath(mappingFolder);
        if (!File.Exists(path)) throw new FileNotFoundException("Ship disposition review file not found.", path);

        var current = ConversionMappingLoader.Load(folder);
        var reviewed = LoadReview(path).Select(x => new ShipDisposition
        {
            SourceId = x.SourceId,
            Kind = x.Kind,
            ProposedTargetId = x.ProposedTargetId,
            Reason = x.Reason,
            Notes = x.Notes
        }).ToList();

        var issues = ShipDispositionValidator.Validate(reviewed).ToList();
        var mappedIds = current.Ships.Select(x => x.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in reviewed.Where(x => mappedIds.Contains(x.SourceId)))
        {
            issues.Add(new ConversionIssue
            {
                Severity = "Error", Category = "Disposition", Code = "DispositionConflictsWithMapping",
                SourceType = "Ship", SourceId = item.SourceId,
                Message = "A live ship mapping already exists for this source ship."
            });
        }

        var merged = current.ShipDispositions.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        foreach (var item in reviewed) merged[item.SourceId] = item;

        return new ShipDispositionApprovalResult
        {
            ReviewPath = path, MappingFolder = folder, CurrentVersion = current.Version,
            TargetVersion = targetVersion, ReviewedCount = reviewed.Count,
            MergedDispositions = merged.Values.OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToList(),
            ValidationIssues = issues
        };
    }

    public static ShipDispositionApprovalResult Apply(string reviewPath, string mappingFolder, string targetVersion)
    {
        var result = Preview(reviewPath, mappingFolder, targetVersion);
        if (result.ValidationIssues.Any(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))) return result;

        var dispositionsPath = Path.Combine(result.MappingFolder, "ship-dispositions.json");
        var manifestPath = Path.Combine(result.MappingFolder, "mapping-set.json");
        var backupFolder = Path.Combine(result.MappingFolder, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupFolder);
        if (File.Exists(dispositionsPath)) File.Copy(dispositionsPath, Path.Combine(backupFolder, "ship-dispositions.json"), true);
        File.Copy(manifestPath, Path.Combine(backupFolder, "mapping-set.json"), true);

        var options = Options(true);
        File.WriteAllText(dispositionsPath, JsonSerializer.Serialize(result.MergedDispositions, options) + Environment.NewLine);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new Manifest { Version = targetVersion }, options) + Environment.NewLine);
        result.Applied = true;
        result.BackupFolder = backupFolder;
        return result;
    }

    private static List<ShipDispositionReviewEntry> LoadReview(string path) =>
        JsonSerializer.Deserialize<List<ShipDispositionReviewEntry>>(File.ReadAllText(path), Options(false))
        ?? throw new InvalidDataException("Unable to deserialize ship disposition review file.");

    private static JsonSerializerOptions Options(bool indented)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = indented, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class Manifest { public string Version { get; init; } = ""; }
}

public sealed class ShipDispositionApprovalResult
{
    public string ReviewPath { get; init; } = "";
    public string MappingFolder { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public int ReviewedCount { get; init; }
    public IReadOnlyList<ShipDisposition> MergedDispositions { get; init; } = Array.Empty<ShipDisposition>();
    public IReadOnlyList<ConversionIssue> ValidationIssues { get; init; } = Array.Empty<ConversionIssue>();
    public bool Applied { get; set; }
    public string BackupFolder { get; set; } = "";
}
