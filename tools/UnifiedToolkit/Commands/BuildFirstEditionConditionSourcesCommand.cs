using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Builds the authoritative First Edition pilot/upgrade-to-condition source registry.</summary>
public static class BuildFirstEditionConditionSourcesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var dataRoot = Path.Combine(repository, "source", "xwing-data", "data");
            var conditionRoot = Path.Combine(repository, "assets", "source", "unified1e", "condition-cards");
            var tokenRoot = Path.Combine(repository, "assets", "source", "unified1e", "condition-tokens");
            var pilotRoot = Path.Combine(repository, "assets", "source", "unified1e", "pilot-cards");
            var upgradeRoot = Path.Combine(repository, "assets", "source", "unified1e", "upgrade-cards");
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "reference", "cards", "condition-sources.json"));

            RequireFile(Path.Combine(dataRoot, "conditions.js"), "Condition definitions");
            var conditions = LoadDefinitions(Path.Combine(dataRoot, "conditions.js"), "condition");
            var pilots = LoadDefinitions(Path.Combine(dataRoot, "pilots.js"), "pilot");
            var upgrades = LoadDefinitions(Path.Combine(dataRoot, "upgrades.js"), "upgrade");
            var conditionByName = conditions.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
            var links = new List<ConditionSourceLink>();

            foreach (var source in pilots.Concat(upgrades).Where(row => row.Conditions.Count > 0))
            {
                var sourcePath = source.SourceType == "pilot"
                    ? PilotArtworkPath(pilotRoot, source)
                    : UpgradeArtworkPath(upgradeRoot, source);
                RequireFile(sourcePath, $"{source.SourceType} source artwork for {source.Name}");

                foreach (var conditionName in source.Conditions)
                {
                    if (!conditionByName.TryGetValue(conditionName, out var condition))
                        throw new InvalidDataException($"{source.SourceType} '{source.Name}' references unknown condition '{conditionName}'.");
                    var conditionFolder = Path.Combine(conditionRoot, condition.Xws);
                    var conditionFace = Path.Combine(conditionFolder, "front.png");
                    var conditionBack = Path.Combine(conditionFolder, "back.png");
                    var tokenFile = condition.Xws is "mimicked" or "shadowed" ? "mimicked-shadowed.png" : $"{condition.Xws}.png";
                    var tokenPath = Path.Combine(tokenRoot, tokenFile);
                    RequireFile(conditionFace, $"{condition.Name} face");
                    RequireFile(conditionBack, $"{condition.Name} back");
                    RequireFile(tokenPath, $"{condition.Name} token");

                    links.Add(new ConditionSourceLink
                    {
                        SourceType = source.SourceType,
                        SourceId = SourceId(source),
                        SourceName = source.Name,
                        SourceXws = source.Xws,
                        SourceShip = source.Ship,
                        SourceFaction = source.Faction,
                        SourceSlot = source.Slot,
                        SourceArtworkRepositoryPath = Relative(repository, sourcePath),
                        ConditionName = condition.Name,
                        ConditionXws = condition.Xws,
                        ConditionFaceRepositoryPath = Relative(repository, conditionFace),
                        ConditionBackRepositoryPath = Relative(repository, conditionBack),
                        ConditionTokenRepositoryPath = Relative(repository, tokenPath)
                    });
                }
            }

            links = links.OrderBy(row => row.SourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.SourceShip, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ConditionName, StringComparer.OrdinalIgnoreCase).ToList();
            var covered = links.Select(row => row.ConditionXws).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var missing = conditions.Where(condition => !covered.Contains(condition.Xws, StringComparer.OrdinalIgnoreCase))
                .Select(condition => condition.Name).ToList();
            if (links.Count != 12 || missing.Count > 0)
                throw new InvalidDataException($"Expected 12 source assignments covering all 10 conditions. Found {links.Count}; missing: {string.Join(", ", missing)}.");

            var registry = new ConditionSourceRegistry
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                ConditionCount = conditions.Count,
                SourceCount = links.Select(row => row.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                AssignmentCount = links.Count,
                PilotAssignmentCount = links.Count(row => row.SourceType == "pilot"),
                UpgradeAssignmentCount = links.Count(row => row.SourceType == "upgrade"),
                ConditionsCovered = covered.Count,
                Assignments = links
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(registry, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Condition Source Registry");
            Console.WriteLine("======================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Condition cards:               {registry.ConditionCount}");
            Console.WriteLine($"Distinct source cards:         {registry.SourceCount}");
            Console.WriteLine($"Pilot-to-condition assignments: {registry.PilotAssignmentCount}");
            Console.WriteLine($"Upgrade-to-condition assignments: {registry.UpgradeAssignmentCount}");
            Console.WriteLine($"Total source assignments:      {registry.AssignmentCount}");
            Console.WriteLine($"Conditions covered:            {registry.ConditionsCovered}");
            Console.WriteLine($"Registry:                      {output}");
            Console.WriteLine();
            Console.WriteLine("Condition sources registered successfully. All linked artwork and tokens were validated.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition condition-source registry failed: {exception.Message}");
            return 1;
        }
    }

    private static List<SourceDefinition> LoadDefinitions(string path, string sourceType)
    {
        RequireFile(path, $"{sourceType} definitions");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(item => new SourceDefinition
        {
            SourceType = sourceType,
            Id = Int(item, "id"),
            Name = Text(item, "name"),
            Xws = Text(item, "xws"),
            Ship = Text(item, "ship"),
            Faction = Text(item, "faction"),
            Slot = Text(item, "slot"),
            Image = Text(item, "image"),
            Conditions = Strings(item, "conditions")
        }).ToList();
    }

    private static string PilotArtworkPath(string root, SourceDefinition source)
    {
        var faction = Normalise(source.Faction);
        var ship = Normalise(source.Ship);
        return Path.Combine(root, faction, ship, Path.GetFileName(source.Image));
    }

    private static string UpgradeArtworkPath(string root, SourceDefinition source)
    {
        var slot = source.Slot.ToLowerInvariant().Replace(' ', '_');
        return Path.Combine(root, slot, $"{source.Xws}.png");
    }

    private static string SourceId(SourceDefinition source) => source.SourceType == "pilot"
        ? $"pilot:{Normalise(source.Faction)}:{Normalise(source.Ship)}:{source.Xws}"
        : $"upgrade:{source.Slot.ToLowerInvariant().Replace(' ', '_')}:{source.Xws}";
    private static string Normalise(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Int(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static List<string> Strings(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(entry => entry.GetString() ?? "").Where(entry => entry.Length > 0).ToList() : new();
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit build-first-edition-condition-sources <repository> [--output <file>]");

    private sealed class SourceDefinition
    {
        public string SourceType { get; init; } = ""; public int Id { get; init; } public string Name { get; init; } = "";
        public string Xws { get; init; } = ""; public string Ship { get; init; } = ""; public string Faction { get; init; } = "";
        public string Slot { get; init; } = ""; public string Image { get; init; } = ""; public List<string> Conditions { get; init; } = new();
    }
}

public sealed class ConditionSourceRegistry
{
    public int SchemaVersion { get; init; } public DateTimeOffset GeneratedUtc { get; init; }
    public int ConditionCount { get; init; } public int SourceCount { get; init; } public int AssignmentCount { get; init; }
    public int PilotAssignmentCount { get; init; } public int UpgradeAssignmentCount { get; init; }
    public int ConditionsCovered { get; init; } public List<ConditionSourceLink> Assignments { get; init; } = new();
}

public sealed class ConditionSourceLink
{
    public string SourceType { get; init; } = ""; public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = ""; public string SourceXws { get; init; } = "";
    public string SourceShip { get; init; } = ""; public string SourceFaction { get; init; } = "";
    public string SourceSlot { get; init; } = ""; public string SourceArtworkRepositoryPath { get; init; } = "";
    public string ConditionName { get; init; } = ""; public string ConditionXws { get; init; } = "";
    public string ConditionFaceRepositoryPath { get; init; } = ""; public string ConditionBackRepositoryPath { get; init; } = "";
    public string ConditionTokenRepositoryPath { get; init; } = "";
}
