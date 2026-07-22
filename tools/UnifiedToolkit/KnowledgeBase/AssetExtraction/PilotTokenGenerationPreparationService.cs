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
        var inventoryPath = Path.Combine(
            root,
            "ukb",
            "reports",
            "pilot-token-inventory-audit",
            "pilot-token-inventory.csv");

        var requiredPath = Path.Combine(
            root,
            "assets",
            "generated",
            "PilotBaseToken",
            "pilot-token-generation-required.json");

        if (!File.Exists(inventoryPath))
        {
            throw new FileNotFoundException(
                "Pilot token inventory report was not found. Run audit-pilot-token-inventory first.",
                inventoryPath);
        }

        if (!File.Exists(requiredPath))
        {
            throw new FileNotFoundException(
                "Generation-required pilot list was not found. Run recover-pilot-tokens first.",
                requiredPath);
        }

        var requiredPilotIds = ReadRequiredPilotIds(requiredPath);
        if (requiredPilotIds.Count == 0)
        {
            throw new InvalidDataException(
                $"The generation-required report contained no pilot IDs: {requiredPath}");
        }

        var rows = ReadCsv(inventoryPath);
        if (rows.Count == 0)
        {
            throw new InvalidDataException(
                $"The pilot token inventory report contained no data rows: {inventoryPath}");
        }

        var rowsByGenerationIdentity = rows
            .Where(row => !IsEpic(row))
            .GroupBy(BuildGenerationIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        var missingInventoryIds = requiredPilotIds
            .Where(id => !rowsByGenerationIdentity.ContainsKey(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingInventoryIds.Count > 0)
        {
            throw new InvalidDataException(
                "The following generation-required pilot IDs were not found in the inventory report: "
                + string.Join(", ", missingInventoryIds));
        }

        var duplicateInventoryIds = requiredPilotIds
            .Where(id => rowsByGenerationIdentity[id].Count != 1)
            .Select(id => $"{id} ({rowsByGenerationIdentity[id].Count} rows)")
            .ToList();

        if (duplicateInventoryIds.Count > 0)
        {
            throw new InvalidDataException(
                "Generation-required pilot IDs must map to exactly one inventory row. Ambiguous IDs: "
                + string.Join(", ", duplicateInventoryIds));
        }

        var tokenRoot = Path.Combine(root, "assets", "generated", "PilotBaseToken");
        var tokenFiles = Directory.Exists(tokenRoot)
            ? Directory.EnumerateFiles(tokenRoot, "*.png", SearchOption.AllDirectories).ToList()
            : new List<string>();

        var output = Path.GetFullPath(
            outputFolder
            ?? Path.Combine(root, "ukb", "reports", "pilot-token-generation-review"));

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        var assets = Path.Combine(output, "assets");
        Directory.CreateDirectory(assets);

        var pilots = new List<object>();
        var withDonors = 0;

        foreach (var requiredPilotId in requiredPilotIds.OrderBy(
                     id => id,
                     StringComparer.OrdinalIgnoreCase))
        {
            var row = rowsByGenerationIdentity[requiredPilotId].Single();

            var faction = row.GetValueOrDefault("Faction", string.Empty);
            var ship = row.GetValueOrDefault("Ship", string.Empty);
            var xws = row.GetValueOrDefault("Xws", string.Empty);
            var inventoryPilotKey = row.GetValueOrDefault("PilotKey", string.Empty);

            var candidates = tokenFiles
                .Where(path => IdentityMatches(path, faction, ship))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var recommended = row.GetValueOrDefault(
                "GenerationDonorRecommendation",
                string.Empty);

            var recommendedFull = recommended.Length == 0
                ? null
                : Path.GetFullPath(
                    Path.Combine(
                        root,
                        recommended.Replace('/', Path.DirectorySeparatorChar)));

            if (recommendedFull is not null && File.Exists(recommendedFull))
            {
                candidates.RemoveAll(path =>
                    path.Equals(recommendedFull, StringComparison.OrdinalIgnoreCase));

                candidates.Insert(0, recommendedFull);
            }

            if (candidates.Count > 0)
            {
                withDonors++;
            }

            var donorCandidates = candidates
                .Select((path, index) =>
                {
                    var previewPath = CopyAsset(
                        assets,
                        path,
                        $"{Safe(requiredPilotId)}-donor-{index + 1}");

                    return new
                    {
                        previewPath,
                        sourceRepositoryPath = Path.GetRelativePath(root, path).Replace('\\', '/'),
                        fileName = Path.GetFileName(path),
                        label = $"Candidate {index + 1} — {Path.GetFileNameWithoutExtension(path)}"
                    };
                })
                .ToList();

            var displayName = row.GetValueOrDefault("Name", string.Empty);
            var cardFull = FindPilotCard(
                root,
                row.GetValueOrDefault("PilotCardPath", string.Empty),
                faction,
                ship,
                displayName,
                xws);

            var copiedCard = cardFull is not null
                ? CopyAsset(assets, cardFull, $"{Safe(requiredPilotId)}-card")
                : string.Empty;

            pilots.Add(new
            {
                pilotId = requiredPilotId,
                inventoryPilotKey,
                targetId = xws,
                displayName,
                faction,
                ship,
                skill = ParseInt(row.GetValueOrDefault("Skill", "0")),
                points = ParseInt(row.GetValueOrDefault("Points", "0")),
                pilotCard = copiedCard,
                pilotCardSourceRepositoryPath = cardFull is null
                    ? string.Empty
                    : Path.GetRelativePath(root, cardFull).Replace('\\', '/'),
                donorCandidates,
                selectedDonor = donorCandidates.FirstOrDefault()?.previewPath ?? string.Empty,
                selectedDonorSourceRepositoryPath =
                    donorCandidates.FirstOrDefault()?.sourceRepositoryPath ?? string.Empty,
                status = candidates.Count > 0 ? "NeedsReview" : "NoDonorAvailable",
                notes = string.Empty
            });
        }

        if (pilots.Count != requiredPilotIds.Count)
        {
            throw new InvalidDataException(
                $"Expected {requiredPilotIds.Count} generation-required pilots, but prepared {pilots.Count}.");
        }

        var plan = new
        {
            schemaVersion = "1.1.0",
            generatedUtc = DateTimeOffset.UtcNow,
            sourceGenerationRequiredReport = Path.GetRelativePath(root, requiredPath)
                .Replace('\\', '/'),
            sourceInventory = Path.GetRelativePath(root, inventoryPath)
                .Replace('\\', '/'),
            pilots
        };

        var planPath = Path.Combine(
            output,
            "pilot-token-generation-plan.template.json");

        File.WriteAllText(
            planPath,
            JsonSerializer.Serialize(plan, JsonOptions),
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(output, "generation-data.js"),
            "window.generationData = "
            + JsonSerializer.Serialize(plan, JsonOptions)
            + ";",
            new UTF8Encoding(false));

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

    private static HashSet<string> ReadRequiredPilotIds(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("pilotIds", out var pilotIds)
            || pilotIds.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The generation-required report must contain a root 'pilotIds' array.");
        }

        return pilotIds
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => NormaliseGenerationIdentity(element.GetString() ?? string.Empty))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildGenerationIdentity(Dictionary<string, string> row)
    {
        return string.Join(
            "::",
            Safe(row.GetValueOrDefault("Faction", string.Empty)),
            Safe(row.GetValueOrDefault("Ship", string.Empty)),
            Safe(row.GetValueOrDefault("Xws", string.Empty)));
    }

    private static string NormaliseGenerationIdentity(string value)
    {
        var parts = value
            .Split(
                "::",
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);

        if (parts.Length != 3)
        {
            throw new InvalidDataException(
                $"Invalid generation-required pilot ID '{value}'. Expected faction::ship::xws.");
        }

        return string.Join("::", parts.Select(Safe));
    }

    private static bool IsEpic(Dictionary<string, string> row)
    {
        return bool.TryParse(
                   row.GetValueOrDefault("IsEpic", "false"),
                   out var isEpic)
               && isEpic;
    }

    private static bool IdentityMatches(
        string path,
        string faction,
        string ship)
    {
        var shipFolder = Path.GetFileName(
            Path.GetDirectoryName(path) ?? string.Empty);

        var factionFolder = Path.GetFileName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(path) ?? string.Empty)
            ?? string.Empty);

        return Normalise(shipFolder) == Normalise(ship)
               && Normalise(factionFolder) == Normalise(faction);
    }

    private static string? FindPilotCard(
        string root,
        string inventoryPath,
        string faction,
        string ship,
        string displayName,
        string xws)
    {
        if (!string.IsNullOrWhiteSpace(inventoryPath))
        {
            var direct = Path.GetFullPath(
                Path.Combine(
                    root,
                    inventoryPath.Replace('/', Path.DirectorySeparatorChar)));

            if (File.Exists(direct))
            {
                return direct;
            }
        }

        var pilotImagesRoot = Path.Combine(
            root,
            "assets",
            "source",
            "xwing-data",
            "images",
            "pilots");

        if (!Directory.Exists(pilotImagesRoot))
        {
            return null;
        }

        var factionFolder = Directory
            .EnumerateDirectories(pilotImagesRoot)
            .FirstOrDefault(path =>
                Normalise(Path.GetFileName(path)) == Normalise(faction));

        if (factionFolder is null)
        {
            return null;
        }

        var shipFolder = Directory
            .EnumerateDirectories(factionFolder)
            .FirstOrDefault(path =>
                Normalise(Path.GetFileName(path)) == Normalise(ship));

        if (shipFolder is null)
        {
            return null;
        }

        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Normalise(displayName),
            Normalise(xws),
            Normalise(RemoveExpansionSuffix(xws))
        };

        var exact = Directory
            .EnumerateFiles(shipFolder, "*.png", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                expectedNames.Contains(
                    Normalise(Path.GetFileNameWithoutExtension(path))));

        if (exact is not null)
        {
            return exact;
        }

        var displayTokens = displayName
            .Split(
                new[] { ' ', '-', '"', '\'', '(', ')', '/' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalise)
            .Where(token => token.Length >= 3)
            .ToList();

        return Directory
            .EnumerateFiles(shipFolder, "*.png", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Name = Normalise(Path.GetFileNameWithoutExtension(path))
            })
            .OrderByDescending(candidate =>
                displayTokens.Count(token => candidate.Name.Contains(token)))
            .ThenBy(candidate => candidate.Name.Length)
            .FirstOrDefault(candidate =>
                displayTokens.Count > 0
                && displayTokens.All(token => candidate.Name.Contains(token)))
            ?.Path;
    }

    private static string RemoveExpansionSuffix(string xws)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            xws ?? string.Empty,
            @"swx\d+$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string CopyAsset(
        string assets,
        string source,
        string name)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(assets, Safe(name) + extension);

        File.Copy(source, destination, overwrite: true);

        return "assets/" + Path.GetFileName(destination);
    }

    private static void CopyUiAssets(string output)
    {
        var source = Path.Combine(
            AppContext.BaseDirectory,
            "KnowledgeBase",
            "AssetExtraction",
            "GenerationReviewAssets");

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"Generation review UI assets were not found: {source}");
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(output, Path.GetFileName(file)),
                overwrite: true);
        }
    }

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return new List<Dictionary<string, string>>();
        }

        var headers = ParseCsvLine(lines[0]);

        return lines
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                var values = ParseCsvLine(line);

                return headers
                    .Select((header, index) => new
                    {
                        header,
                        value = index < values.Count
                            ? values[index]
                            : string.Empty
                    })
                    .ToDictionary(
                        pair => pair.header,
                        pair => pair.value,
                        StringComparer.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var value = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (quoted
                    && index + 1 < line.Length
                    && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                result.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        result.Add(value.ToString());
        return result;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var result)
            ? result
            : 0;
    }

    private static string Safe(string value)
    {
        return new string(
            (value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string Normalise(string value)
    {
        return Safe(value);
    }
}
