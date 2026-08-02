using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class Unified1eAssetMigrationPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Unified1eAssetMigrationPlan Build(string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var inventoryPath = Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "model-inventory", "ship-model-inventory.json");
        if (!File.Exists(inventoryPath))
            throw new FileNotFoundException("Run audit-ship-model-inventory before planning the migration.", inventoryPath);

        var inventory = JsonSerializer.Deserialize<ShipModelInventoryManifest>(File.ReadAllText(inventoryPath), JsonOptions)
            ?? throw new InvalidDataException("Could not parse the ship-model inventory.");
        var folderMap = LoadFolderMap(repositoryRoot);
        var sizeMap = LoadSizeMap(repositoryRoot);
        var plan = new Unified1eAssetMigrationPlan
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalise(repositoryRoot)
        };

        var usedFolders = inventory.Entries
            .Where(e => !e.UsageStatus.Equals("ReviewCandidate", StringComparison.OrdinalIgnoreCase))
            .Select(e => ParseShipFolder(e.RepositoryPath))
            .Where(x => x is not null)
            .Cast<(string Class, string Folder)>()
            .Distinct()
            .OrderBy(x => x.Class).ThenBy(x => x.Folder)
            .ToList();

        foreach (var item in usedFolders)
        {
            var source = $"assets/source/unified25/assets/ships-v2/{item.Class}/{item.Folder}";
            var canonical = folderMap.TryGetValue(item.Folder, out var mapped) ? mapped : item.Folder;
            var size = sizeMap.TryGetValue(canonical, out var mappedSize) ? mappedSize : string.Empty;
            var entry = FolderEntry(repositoryRoot, "ShipFolder", source,
                size.Length == 0 ? string.Empty : $"assets/source/unified1e/ships/{size}/{canonical}");
            entry.SourceFolderName = item.Folder;
            entry.CanonicalFirstEditionId = canonical;
            entry.CurrentFolderClass = item.Class;
            entry.FirstEditionBaseSize = size;
            if (size.Length == 0)
            {
                entry.Status = "ManualReviewRequired";
                entry.Reasons.Add("No First Edition base size could be resolved from the semantic ship mappings.");
            }
            plan.Entries.Add(entry);
        }

        MarkDestinationConflicts(plan.Entries.Where(e => e.Kind == "ShipFolder"));

        AddBaseFolder(plan, repositoryRoot, "small", "small");
        AddBaseFolder(plan, repositoryRoot, "large", "large");
        AddBaseFolder(plan, repositoryRoot, "huge", "epic");

        AddReferencedFiles(plan, repositoryRoot);
        AddWholeFolder(plan, repositoryRoot, "DialTextures", "assets/source/first-edition-dial-textures", "assets/source/unified1e/dial-textures");

        plan.ShipFolders = plan.Entries.Count(e => e.Kind == "ShipFolder");
        plan.BaseFolders = plan.Entries.Count(e => e.Kind == "BaseFolder");
        plan.AdditionalFiles = plan.Entries.Count(e => e.Kind is "PilotCard" or "PilotToken" or "DialTextures");
        plan.Ready = plan.Entries.Count(e => e.Status == "Ready");
        plan.ManualReviewRequired = plan.Entries.Count(e => e.Status == "ManualReviewRequired");
        plan.Conflicts = plan.Entries.Count(e => e.Status == "Conflict");
        return plan;
    }

    private static Dictionary<string, string> LoadFolderMap(string repositoryRoot)
    {
        var path = ResolveConversionData(repositoryRoot, "ship-folder-map.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions)
            ?? new(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> LoadSizeMap(string repositoryRoot)
    {
        var path = ResolveConversionData(repositoryRoot, "ships.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateArray()
            .Where(x => x.TryGetProperty("targetId", out _) && x.TryGetProperty("size", out _))
            .GroupBy(x => x.GetProperty("targetId").GetString() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().GetProperty("size").GetString() ?? "", StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveConversionData(string repositoryRoot, string file)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition", file),
            Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition", file)
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Conversion data file was not found: {file}");
    }

    private static (string Class, string Folder)? ParseShipFolder(string path)
    {
        var match = Regex.Match(Normalise(path), @"ships-v2/(small|medium|large)/([^/]+)/", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value.ToLowerInvariant()) : null;
    }

    private static Unified1eAssetMigrationEntry FolderEntry(string root, string kind, string source, string destination)
    {
        var full = Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar));
        var files = Directory.Exists(full) ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).ToList() : [];
        var entry = new Unified1eAssetMigrationEntry
        {
            Kind = kind,
            SourcePath = source,
            DestinationPath = destination,
            FileCount = files.Count,
            SizeBytes = files.Sum(f => new FileInfo(f).Length)
        };
        if (!Directory.Exists(full))
        {
            entry.Status = "ManualReviewRequired";
            entry.Reasons.Add("Source folder does not exist.");
        }
        return entry;
    }

    private static void MarkDestinationConflicts(IEnumerable<Unified1eAssetMigrationEntry> entries)
    {
        foreach (var group in entries.Where(e => e.DestinationPath.Length > 0).GroupBy(e => e.DestinationPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var entry in group)
            {
                entry.Status = "Conflict";
                entry.Reasons.Add("Multiple Unified 2.5 source folders resolve to the same First Edition destination.");
            }
        }
    }

    private static void AddBaseFolder(Unified1eAssetMigrationPlan plan, string root, string sourceClass, string targetClass)
    {
        var entry = FolderEntry(root, "BaseFolder",
            $"assets/source/unified25/assets/ships-v2/bases/{sourceClass}",
            $"assets/source/unified1e/bases/{targetClass}");
        entry.CurrentFolderClass = sourceClass;
        entry.FirstEditionBaseSize = targetClass;
        if (sourceClass == "huge") entry.Reasons.Add("Unified 2.5 Huge terminology maps to First Edition Epic.");
        plan.Entries.Add(entry);
    }

    private static void AddReferencedFiles(Unified1eAssetMigrationPlan plan, string root)
    {
        var planPath = Path.Combine(root, "_unifiedtoolkit_reports", "phase11", "ship-package-planning", "ship-package-plans.json");
        if (!File.Exists(planPath)) return;
        var text = File.ReadAllText(planPath);
        AddMatches(plan, root, text, "assets/source/xwing-data/images/pilots/[^\\\"'\\s]+", "PilotCard", "assets/source/xwing-data/images/pilots/", "assets/source/unified1e/pilot-cards/");
        AddMatches(plan, root, text, "assets/generated/PilotBaseToken/[^\\\"'\\s]+", "PilotToken", "assets/generated/PilotBaseToken/", "assets/source/unified1e/pilot-tokens/");
    }

    private static void AddMatches(Unified1eAssetMigrationPlan plan, string root, string text, string pattern, string kind, string prefix, string destinationPrefix)
    {
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
        {
            var source = Normalise(match.Value).TrimEnd(',', '}', ']');
            if (plan.Entries.Any(e => e.SourcePath.Equals(source, StringComparison.OrdinalIgnoreCase))) continue;
            var full = Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar));
            var entry = new Unified1eAssetMigrationEntry
            {
                Kind = kind,
                SourcePath = source,
                DestinationPath = destinationPrefix + source[prefix.Length..],
                FileCount = File.Exists(full) ? 1 : 0,
                SizeBytes = File.Exists(full) ? new FileInfo(full).Length : 0
            };
            if (!File.Exists(full)) { entry.Status = "ManualReviewRequired"; entry.Reasons.Add("Referenced source file does not exist."); }
            plan.Entries.Add(entry);
        }
    }

    private static void AddWholeFolder(Unified1eAssetMigrationPlan plan, string root, string kind, string source, string destination) =>
        plan.Entries.Add(FolderEntry(root, kind, source, destination));

    private static string Normalise(string value) => value.Replace('\\', '/');
}
