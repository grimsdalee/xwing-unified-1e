using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotMappingApprovalResult
{
    public int CanonicalCount { get; init; }
    public int AlternateCount { get; init; }
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();
    public bool Applied { get; init; }
    public string BackupFolder { get; init; } = "";
}

public static class PilotMappingApprovalService
{
    public static PilotMappingApprovalResult Execute(string canonicalProposalPath, string alternateProposalPath, string mappingFolder, string targetVersion, bool apply)
    {
        var options = Options();
        var canonical = JsonSerializer.Deserialize<List<PilotMapping>>(File.ReadAllText(canonicalProposalPath), options) ?? new();
        var alternates = JsonSerializer.Deserialize<List<PilotSourceAlternate>>(File.ReadAllText(alternateProposalPath), options) ?? new();
        var issues = PilotMappingValidator.Validate(canonical, alternates).ToList();

        var pilotsPath = Path.Combine(mappingFolder, "pilots.json");
        var alternatesPath = Path.Combine(mappingFolder, "pilot-source-alternates.json");
        var manifestPath = Path.Combine(mappingFolder, "mapping-set.json");
        if (File.Exists(pilotsPath) && (JsonSerializer.Deserialize<List<PilotMapping>>(File.ReadAllText(pilotsPath), options)?.Count ?? 0) > 0)
            issues.Add("Live pilots.json is not empty. Pilot promotion currently supports the initial import only.");

        if (!apply || issues.Count > 0)
            return new PilotMappingApprovalResult { CanonicalCount = canonical.Count, AlternateCount = alternates.Count, ValidationIssues = issues };

        Directory.CreateDirectory(mappingFolder);
        var backup = Path.Combine(mappingFolder, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach (var file in new[] { pilotsPath, alternatesPath, manifestPath })
            if (File.Exists(file)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);

        var writeOptions = new JsonSerializerOptions { WriteIndented = true };
        writeOptions.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(pilotsPath, JsonSerializer.Serialize(canonical, writeOptions));
        File.WriteAllText(alternatesPath, JsonSerializer.Serialize(alternates, writeOptions));
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { version = targetVersion }, writeOptions));

        return new PilotMappingApprovalResult { CanonicalCount = canonical.Count, AlternateCount = alternates.Count, ValidationIssues = issues, Applied = true, BackupFolder = backup };
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
