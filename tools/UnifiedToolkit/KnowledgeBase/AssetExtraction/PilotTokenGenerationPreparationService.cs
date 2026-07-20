using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenGenerationPreparationResult
{
    public int PilotCount { get; init; }
    public int PilotsWithDonors { get; init; }
    public int PilotsWithoutDonors { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string PlanFile { get; init; } = string.Empty;
    public string HtmlFile { get; init; } = string.Empty;
}

public sealed class PilotTokenGenerationPreparationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PilotTokenGenerationPreparationResult Prepare(string repositoryRoot, string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var inventoryPath = Path.Combine(root, "ukb", "reports", "pilot-token-inventory-audit", "pilot-token-inventory.csv");
        var requiredPath = Path.Combine(root, "assets", "generated", "PilotBaseToken", "pilot-token-generation-required.json");
        if (!File.Exists(inventoryPath)) throw new FileNotFoundException("Pilot token inventory report was not found. Run audit-pilot-token-inventory first.", inventoryPath);
        if (!File.Exists(requiredPath)) throw new FileNotFoundException("Generation-required pilot list was not found. Run recover-pilot-tokens first.", requiredPath);

        var required = JsonDocument.Parse(File.ReadAllText(requiredPath, Encoding.UTF8)).RootElement
            .GetProperty("pilotIds").EnumerateArray().Select(x => x.GetString() ?? string.Empty)
            .Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = ReadCsv(inventoryPath);
        var tokenRoot = Path.Combine(root, "assets", "generated", "PilotBaseToken");
        var tokenFiles = Directory.EnumerateFiles(tokenRoot, "*.png", SearchOption.AllDirectories).ToList();
        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-token-generation-review"));
        var assets = Path.Combine(output, "assets");
        Directory.CreateDirectory(assets);

        var pilots = new List<object>();
        var withDonors = 0;
        foreach (var row in rows.Where(r => required.Contains(r.GetValueOrDefault("PilotKey", string.Empty))))
        {
            var faction = row.GetValueOrDefault("Faction", string.Empty);
            var ship = row.GetValueOrDefault("Ship", string.Empty);
            var pilotKey = row.GetValueOrDefault("PilotKey", string.Empty);
            var candidates = tokenFiles.Where(path => IdentityMatches(path, faction, ship))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToList();
            var recommended = row.GetValueOrDefault("GenerationDonorRecommendation", string.Empty);
            var recommendedFull = recommended.Length == 0 ? null : Path.GetFullPath(Path.Combine(root, recommended.Replace('/', Path.DirectorySeparatorChar)));
            if (recommendedFull is not null && File.Exists(recommendedFull))
            {
                candidates.RemoveAll(x => x.Equals(recommendedFull, StringComparison.OrdinalIgnoreCase));
                candidates.Insert(0, recommendedFull);
            }
            if (candidates.Count > 0) withDonors++;

            var copiedCandidates = candidates.Select((path, index) => CopyAsset(root, assets, path, $"{Safe(pilotKey)}-donor-{index + 1}")).ToList();
            var cardPath = row.GetValueOrDefault("PilotCardPath", string.Empty);
            var cardFull = cardPath.Length == 0 ? null : Path.GetFullPath(Path.Combine(root, cardPath.Replace('/', Path.DirectorySeparatorChar)));
            var copiedCard = cardFull is not null && File.Exists(cardFull) ? CopyAsset(root, assets, cardFull, $"{Safe(pilotKey)}-card") : string.Empty;

            pilots.Add(new
            {
                pilotId = pilotKey,
                targetId = row.GetValueOrDefault("Xws", string.Empty),
                displayName = row.GetValueOrDefault("Name", string.Empty),
                faction,
                ship,
                skill = ParseInt(row.GetValueOrDefault("Skill", "0")),
                points = ParseInt(row.GetValueOrDefault("Points", "0")),
                pilotCard = copiedCard,
                donorCandidates = copiedCandidates,
                selectedDonor = copiedCandidates.FirstOrDefault() ?? string.Empty,
                status = candidates.Count > 0 ? "NeedsReview" : "NoDonorAvailable",
                notes = string.Empty
            });
        }

        var plan = new { schemaVersion = "1.0.0", generatedUtc = DateTimeOffset.UtcNow, pilots };
        var planPath = Path.Combine(output, "pilot-token-generation-plan.template.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "generation-data.js"), "window.generationData = " + JsonSerializer.Serialize(plan, JsonOptions) + ";", new UTF8Encoding(false));
        CopyUiAssets(output);

        return new PilotTokenGenerationPreparationResult
        {
            PilotCount = pilots.Count,
            PilotsWithDonors = withDonors,
            PilotsWithoutDonors = pilots.Count - withDonors,
            OutputFolder = output,
            PlanFile = planPath,
            HtmlFile = Path.Combine(output, "index.html")
        };
    }

    private static bool IdentityMatches(string path, string faction, string ship)
    {
        var shipFolder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        var factionFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path) ?? string.Empty) ?? string.Empty);
        return Normalise(shipFolder) == Normalise(ship) && Normalise(factionFolder) == Normalise(faction);
    }

    private static string CopyAsset(string root, string assets, string source, string name)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(assets, Safe(name) + extension);
        File.Copy(source, destination, true);
        return "assets/" + Path.GetFileName(destination);
    }

    private static void CopyUiAssets(string output)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "KnowledgeBase", "AssetExtraction", "GenerationReviewAssets");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Generation review UI assets were not found: {source}");
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(output, Path.GetFileName(file)), true);
    }

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) return new();
        var headers = ParseCsvLine(lines[0]);
        return lines.Skip(1).Where(x => x.Length > 0).Select(line =>
        {
            var values = ParseCsvLine(line);
            return headers.Select((header, i) => new { header, value = i < values.Count ? values[i] : string.Empty })
                .ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);
        }).ToList();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>(); var value = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        result.Add(value.ToString()); return result;
    }

    private static int ParseInt(string value) => int.TryParse(value, out var result) ? result : 0;
    private static string Safe(string value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Normalise(string value) => Safe(value);
}
