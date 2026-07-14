using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class UpgradeMappingApprovalResult
{
    public int CanonicalCount { get; init; }
    public int AlternateCount { get; init; }
    public int DispositionCount { get; init; }
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();
    public bool Applied { get; init; }
    public string BackupFolder { get; init; } = "";
}

public static class UpgradeMappingApprovalService
{
    public static UpgradeMappingApprovalResult Execute(string canonicalPath, string alternatePath, string candidatesCsvPath, string mappingFolder, string targetVersion, bool apply)
    {
        var options = Options();
        var canonical = JsonSerializer.Deserialize<List<UpgradeMapping>>(File.ReadAllText(canonicalPath), options) ?? new();
        var alternates = JsonSerializer.Deserialize<List<UpgradeSourceAlternate>>(File.ReadAllText(alternatePath), options) ?? new();
        var dispositions = ReadDispositions(candidatesCsvPath);
        var issues = UpgradeMappingValidator.Validate(canonical, alternates).ToList();

        var classified = canonical.Select(x => x.SourceId).Concat(alternates.Select(x => x.SourceId)).Concat(dispositions.Select(x => x.SourceId)).ToList();
        foreach (var group in classified.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            issues.Add($"Source upgrade '{group.Key}' is classified more than once.");

        var upgradesPath = Path.Combine(mappingFolder, "upgrades.json");
        var alternatesPath = Path.Combine(mappingFolder, "upgrade-source-alternates.json");
        var dispositionsPath = Path.Combine(mappingFolder, "upgrade-dispositions.json");
        var manifestPath = Path.Combine(mappingFolder, "mapping-set.json");

        if (File.Exists(upgradesPath) && (JsonSerializer.Deserialize<List<UpgradeMapping>>(File.ReadAllText(upgradesPath), options)?.Count ?? 0) > 0)
            issues.Add("Live upgrades.json is not empty. Initial upgrade approval refuses to overwrite existing live mappings.");

        if (!apply || issues.Count > 0)
            return new UpgradeMappingApprovalResult { CanonicalCount = canonical.Count, AlternateCount = alternates.Count, DispositionCount = dispositions.Count, ValidationIssues = issues };

        Directory.CreateDirectory(mappingFolder);
        var backup = Path.Combine(mappingFolder, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach (var file in new[] { upgradesPath, alternatesPath, dispositionsPath, manifestPath })
            if (File.Exists(file)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);

        var writeOptions = new JsonSerializerOptions { WriteIndented = true };
        writeOptions.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(upgradesPath, JsonSerializer.Serialize(canonical, writeOptions));
        File.WriteAllText(alternatesPath, JsonSerializer.Serialize(alternates, writeOptions));
        File.WriteAllText(dispositionsPath, JsonSerializer.Serialize(dispositions, writeOptions));
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { version = targetVersion }, writeOptions));

        return new UpgradeMappingApprovalResult { CanonicalCount = canonical.Count, AlternateCount = alternates.Count, DispositionCount = dispositions.Count, ValidationIssues = issues, Applied = true, BackupFolder = backup };
    }

    private static List<UpgradeDisposition> ReadDispositions(string path)
    {
        var rows = ReadCsv(path);
        var result = new List<UpgradeDisposition>();
        foreach (var row in rows)
        {
            if (!row.TryGetValue("Status", out var status) || status is not ("Ambiguous" or "NotInOfficialDataset")) continue;
            result.Add(new UpgradeDisposition
            {
                SourceId = row.GetValueOrDefault("SourceId", ""),
                Kind = status == "Ambiguous" ? UpgradeDispositionKind.Ambiguous : UpgradeDispositionKind.NotInOfficialDataset,
                Reason = row.GetValueOrDefault("Notes", "")
            });
        }
        return result;
    }

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        using var reader = new StreamReader(path);
        var header = ParseCsvLine(reader.ReadLine() ?? throw new InvalidDataException("Candidates CSV is empty."));
        var rows = new List<Dictionary<string, string>>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var values = ParseCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++) row[header[i]] = i < values.Count ? values[i] : "";
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        result.Add(value.ToString());
        return result;
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
