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
        var folderMetadata = LoadFolderMetadata(repositoryRoot);
        var sizeMap = LoadSizeMap(repositoryRoot);
        var plan = new Unified1eAssetMigrationPlan
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalise(repositoryRoot)
        };

        var usedFolders = inventory.Entries
            .Where(e => !e.UsageStatus.Equals(
                "ReviewCandidate",
                StringComparison.OrdinalIgnoreCase))
            .Select(e => ParseShipFolder(e.RepositoryPath))
            .Where(x => x.HasValue)
            .Select(x => x.GetValueOrDefault())
            .Distinct()
            .OrderBy(x => x.Class)
            .ThenBy(x => x.Folder)
            .ToList();

        foreach (var item in usedFolders)
        {
            var source = $"assets/source/unified25/assets/ships-v2/{item.Class}/{item.Folder}";
            var metadata = folderMetadata.TryGetValue(
                item.Folder,
                out var mappedMetadata)
                ? mappedMetadata
                : new ShipFolderMigrationMetadata
                {
                    Folder = item.Folder
                };

            var canonical = string.IsNullOrWhiteSpace(metadata.Folder)
                ? item.Folder
                : metadata.Folder;

            var size = !string.IsNullOrWhiteSpace(metadata.BaseSize)
                ? metadata.BaseSize
                : sizeMap.TryGetValue(canonical, out var mappedSize)
                    ? mappedSize
                    : string.Empty;
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

        AddEpicShipFolders(
            plan,
            repositoryRoot,
            folderMetadata);

        MarkDestinationConflicts(plan.Entries.Where(e => e.Kind == "ShipFolder"));

        AddBaseFolder(plan, repositoryRoot, "small", "small");
        AddBaseFolder(plan, repositoryRoot, "large", "large");
        AddBaseFolder(plan, repositoryRoot, "huge", "epic");
        AddPegModels(plan, repositoryRoot);

        AddReferencedFiles(plan, repositoryRoot);
        AddEffectivePrototypeDependencies(
            plan,
            repositoryRoot);
        AddWholeFolder(plan, repositoryRoot, "DialTextures", "assets/source/first-edition-dial-textures", "assets/source/unified1e/dial-textures");

        plan.ShipFolders = plan.Entries.Count(e => e.Kind == "ShipFolder");
        plan.BaseFolders = plan.Entries.Count(e => e.Kind == "BaseFolder");
        plan.AdditionalFiles = plan.Entries.Count(e =>
            e.Kind is "PilotCard"
                or "PilotToken"
                or "DialTextures"
                or "PegModel"
                or "PrototypeDependency");
        plan.Ready = plan.Entries.Count(e => e.Status == "Ready");
        plan.ManualReviewRequired = plan.Entries.Count(e => e.Status == "ManualReviewRequired");
        plan.Conflicts = plan.Entries.Count(e => e.Status == "Conflict");
        return plan;
    }

    private static Dictionary<string, ShipFolderMigrationMetadata>
        LoadFolderMetadata(string repositoryRoot)
    {
        var path = ResolveConversionData(
            repositoryRoot,
            "ship-folder-map.json");

        var metadata = JsonSerializer.Deserialize<
            Dictionary<string, ShipFolderMigrationMetadata>>(
                File.ReadAllText(path),
                JsonOptions)
            ?? new Dictionary<string, ShipFolderMigrationMetadata>(
                StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, ShipFolderMigrationMetadata>(
            metadata,
            StringComparer.OrdinalIgnoreCase);
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

    private static void AddEpicShipFolders(
        Unified1eAssetMigrationPlan plan,
        string repositoryRoot,
        IReadOnlyDictionary<string, ShipFolderMigrationMetadata> folderMetadata)
    {
        var hugeRootRelative =
            "assets/source/unified25/assets/ships-v2/huge";
        var hugeRoot = Path.Combine(
            repositoryRoot,
            hugeRootRelative.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(hugeRoot))
            return;

        foreach (var directory in Directory.EnumerateDirectories(hugeRoot)
                     .OrderBy(
                         path => Path.GetFileName(path),
                         StringComparer.OrdinalIgnoreCase))
        {
            var sourceFolder = Path.GetFileName(directory);
            var metadata = folderMetadata.TryGetValue(
                sourceFolder,
                out var mappedMetadata)
                ? mappedMetadata
                : new ShipFolderMigrationMetadata
                {
                    Folder = sourceFolder,
                    BaseSize = "epic"
                };

            var canonical = string.IsNullOrWhiteSpace(metadata.Folder)
                ? sourceFolder
                : metadata.Folder;

            var source =
                $"{hugeRootRelative}/{sourceFolder}";
            var destination =
                $"assets/source/unified1e/ships/epic/{canonical}";

            var entry = FolderEntry(
                repositoryRoot,
                "ShipFolder",
                source,
                destination);
            entry.SourceFolderName = sourceFolder;
            entry.CanonicalFirstEditionId = canonical;
            entry.CurrentFolderClass = "huge";
            entry.FirstEditionBaseSize = "epic";
            entry.Reasons.Add(
                "Unified 2.5 Huge terminology maps to First Edition Epic.");
            plan.Entries.Add(entry);
        }
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

    private static void AddPegModels(
        Unified1eAssetMigrationPlan plan,
        string root)
    {
        AddPegModel(plan, root, "small.obj", "small.obj");
        AddPegModel(plan, root, "large.obj", "large.obj");
        AddPegModel(plan, root, "bwing.obj", "bwing.obj");
        AddPegModel(plan, root, "huge.obj", "epic.obj",
            "Unified 2.5 Huge terminology maps to First Edition Epic.");

        // medium.obj is deliberately excluded. First Edition supports only
        // Small, Large and Epic ship base sizes.
    }

    private static void AddPegModel(
        Unified1eAssetMigrationPlan plan,
        string root,
        string sourceFileName,
        string destinationFileName,
        string? reason = null)
    {
        var source =
            $"assets/source/unified25/assets/ships-v2/bases/pegs/{sourceFileName}";
        var destination =
            $"assets/source/unified1e/bases/pegs/{destinationFileName}";
        var fullPath = Path.Combine(
            root,
            source.Replace('/', Path.DirectorySeparatorChar));

        var entry = new Unified1eAssetMigrationEntry
        {
            Kind = "PegModel",
            SourcePath = source,
            DestinationPath = destination,
            SourceFolderName = "pegs",
            CanonicalFirstEditionId = Path.GetFileNameWithoutExtension(
                destinationFileName),
            CurrentFolderClass = sourceFileName.Equals(
                "huge.obj",
                StringComparison.OrdinalIgnoreCase)
                ? "huge"
                : Path.GetFileNameWithoutExtension(sourceFileName),
            FirstEditionBaseSize = destinationFileName.Equals(
                "epic.obj",
                StringComparison.OrdinalIgnoreCase)
                ? "epic"
                : Path.GetFileNameWithoutExtension(destinationFileName),
            FileCount = File.Exists(fullPath) ? 1 : 0,
            SizeBytes = File.Exists(fullPath)
                ? new FileInfo(fullPath).Length
                : 0
        };

        if (!File.Exists(fullPath))
        {
            entry.Status = "ManualReviewRequired";
            entry.Reasons.Add("Peg source file does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(reason))
            entry.Reasons.Add(reason);

        plan.Entries.Add(entry);
    }

    private static void AddReferencedFiles(
        Unified1eAssetMigrationPlan plan,
        string root)
    {
        var planPath = Path.Combine(
            root,
            "_unifiedtoolkit_reports",
            "phase11",
            "ship-package-planning",
            "ship-package-plans.json");

        if (!File.Exists(planPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(planPath));
        var stringValues = EnumerateStringValues(document.RootElement);

        AddStructuredPaths(
            plan,
            root,
            stringValues,
            "assets/source/xwing-data/images/pilots/",
            "PilotCard",
            "assets/source/unified1e/pilot-cards/");

        AddStructuredPaths(
            plan,
            root,
            stringValues,
            "assets/generated/PilotBaseToken/",
            "PilotToken",
            "assets/source/unified1e/pilot-tokens/");
    }

    private static IEnumerable<string> EnumerateStringValues(
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var nestedValue in EnumerateStringValues(property.Value))
                        yield return nestedValue;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nestedValue in EnumerateStringValues(item))
                        yield return nestedValue;
                }
                break;

            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
                break;
        }
    }

    private static void AddStructuredPaths(
        Unified1eAssetMigrationPlan plan,
        string root,
        IEnumerable<string> values,
        string sourcePrefix,
        string kind,
        string destinationPrefix)
    {
        foreach (var value in values)
        {
            var source = TryExtractRepositoryPath(value, sourcePrefix);
            if (source is null)
                continue;

            if (plan.Entries.Any(entry => entry.SourcePath.Equals(
                    source,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fullPath = Path.Combine(
                root,
                source.Replace('/', Path.DirectorySeparatorChar));

            var entry = new Unified1eAssetMigrationEntry
            {
                Kind = kind,
                SourcePath = source,
                DestinationPath = destinationPrefix + source[sourcePrefix.Length..],
                FileCount = File.Exists(fullPath) ? 1 : 0,
                SizeBytes = File.Exists(fullPath)
                    ? new FileInfo(fullPath).Length
                    : 0
            };

            if (!File.Exists(fullPath))
            {
                entry.Status = "ManualReviewRequired";
                entry.Reasons.Add("Referenced source file does not exist.");
            }

            plan.Entries.Add(entry);
        }
    }

    private static string? TryExtractRepositoryPath(
        string value,
        string sourcePrefix)
    {
        var normalised = Uri.UnescapeDataString(Normalise(value));
        var index = normalised.IndexOf(
            sourcePrefix,
            StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return null;

        var source = normalised[index..];
        var queryIndex = source.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            source = source[..queryIndex];

        return source.Trim().Trim('"', '\'');
    }

    private static void AddEffectivePrototypeDependencies(
        Unified1eAssetMigrationPlan plan,
        string repositoryRoot)
    {
        var auditPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase13",
            "prototype-asset-dependencies",
            "prototype-asset-dependencies.json");

        if (!File.Exists(auditPath))
            return;

        using var document = JsonDocument.Parse(
            File.ReadAllText(auditPath));

        if (!document.RootElement.TryGetProperty(
                "entries",
                out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var dependency in entries.EnumerateArray())
        {
            var category = ReadString(
                dependency,
                "category");
            var scope = ReadString(
                dependency,
                "scope");

            if (!category.Equals(
                    "Unified25Dependency",
                    StringComparison.OrdinalIgnoreCase)
                || scope.Equals(
                    "Ship",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = ReadString(
                dependency,
                "repositoryPath");
            var destination = ReadString(
                dependency,
                "suggestedDestination");

            if (source.Length == 0 || destination.Length == 0)
                continue;

            if (plan.Entries.Any(entry =>
                    SourceCoversPath(entry.SourcePath, source)
                    || entry.SourcePath.Equals(
                        source,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fullPath = Path.Combine(
                repositoryRoot,
                source.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            var entry = new Unified1eAssetMigrationEntry
            {
                Kind = "PrototypeDependency",
                SourcePath = source,
                DestinationPath = destination,
                FileCount = File.Exists(fullPath) ? 1 : 0,
                SizeBytes = File.Exists(fullPath)
                    ? new FileInfo(fullPath).Length
                    : 0
            };

            entry.Reasons.Add(
                $"Effective prototype dependency ({scope}).");

            if (!File.Exists(fullPath))
            {
                entry.Status = "ManualReviewRequired";
                entry.Reasons.Add(
                    "Effective prototype dependency source file does not exist.");
            }

            if (plan.Entries.Any(existing =>
                    existing.DestinationPath.Equals(
                        destination,
                        StringComparison.OrdinalIgnoreCase)
                    && !existing.SourcePath.Equals(
                        source,
                        StringComparison.OrdinalIgnoreCase)))
            {
                entry.Status = "Conflict";
                entry.Reasons.Add(
                    "Another source resolves to the same dependency destination.");
            }

            plan.Entries.Add(entry);
        }
    }

    private static string ReadString(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(
            propertyName,
            out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool SourceCoversPath(
        string plannedSource,
        string dependencySource)
    {
        if (plannedSource.Length == 0)
            return false;

        return dependencySource.StartsWith(
            plannedSource.TrimEnd('/') + "/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AddWholeFolder(Unified1eAssetMigrationPlan plan, string root, string kind, string source, string destination) =>
        plan.Entries.Add(FolderEntry(root, kind, source, destination));

    private static string Normalise(string value) => value.Replace('\\', '/');

    private sealed class ShipFolderMigrationMetadata
    {
        public string Folder { get; set; } = string.Empty;
        public string BaseSize { get; set; } = string.Empty;
    }
}
