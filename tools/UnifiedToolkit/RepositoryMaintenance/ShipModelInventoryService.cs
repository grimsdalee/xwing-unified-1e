using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class ShipModelInventoryService
{
    private const string ModelRootRelative =
        "assets/source/unified25/assets/ships-v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> ProtectedProductionModels =
    [
        "assets/source/unified25/assets/ships-v2/small/" +
        "tieagaggressor/tieagaggressor.obj",
        "assets/source/unified25/assets/ships-v2/medium/" +
        "aggressorassaultfighter/aggressor.obj"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> MultipartSets =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["T-65 X-Wing"] =
            [
                "assets/source/unified25/assets/ships-v2/small/t65xwing/xwingbasev3.obj",
                "assets/source/unified25/assets/ships-v2/small/t65xwing/xwingopenv3.obj",
                "assets/source/unified25/assets/ships-v2/small/t65xwing/xwingclosedv3.obj"
            ],
            ["T-70 X-Wing"] =
            [
                "assets/source/unified25/assets/ships-v2/small/t70xwing/t70_basev2.obj",
                "assets/source/unified25/assets/ships-v2/small/t70xwing/t70_openv2.obj",
                "assets/source/unified25/assets/ships-v2/small/t70xwing/t70_closedv2.obj"
            ],
            ["A/SF-01 B-Wing"] =
            [
                "assets/source/unified25/assets/ships-v2/small/asf01bwing/bwing-base.obj",
                "assets/source/unified25/assets/ships-v2/small/asf01bwing/bwing-open.obj",
                "assets/source/unified25/assets/ships-v2/small/asf01bwing/bwing-closed.obj"
            ],
            ["UT-60D U-Wing"] =
            [
                "assets/source/unified25/assets/ships-v2/medium/ut60duwing/UwingOpen.obj",
                "assets/source/unified25/assets/ships-v2/medium/ut60duwing/UwingClose.obj"
            ]
        };

    public ShipModelInventoryManifest Audit(string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var modelRoot = Path.Combine(
            repositoryRoot,
            ModelRootRelative.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(modelRoot))
        {
            throw new DirectoryNotFoundException(
                $"Unified 2.5 ship-model root was not found: {modelRoot}");
        }

        var included = new[] { "small", "medium", "large" };
        var usages = new Dictionary<string, ShipModelUsage>(
            StringComparer.OrdinalIgnoreCase);
        var missingConfigured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var historicalObsolete = LoadHistoricalObsoleteModelPaths(repositoryRoot);

        CollectConfiguredModelUsages(
            repositoryRoot,
            usages,
            missingConfigured,
            historicalObsolete);
        CollectGeneratedSaveUsages(repositoryRoot, usages);
        CollectPipelineInputUsages(repositoryRoot, usages);
        ProtectMultipartSets(usages);
        ProtectProductionModels(usages);

        var files = included
            .Select(folder => Path.Combine(modelRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(
                folder,
                "*.obj",
                SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = files
            .Select(path => BuildEntry(repositoryRoot, path, usages))
            .ToList();

        ApplyDuplicateInformation(entries);

        var multipartErrors = ValidateMultipartSets(repositoryRoot, entries);

        return new ShipModelInventoryManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalise(repositoryRoot),
            IncludedFolders = included
                .Select(folder => $"{ModelRootRelative}/{folder}")
                .ToList(),
            ExcludedFolders =
            [
                $"{ModelRootRelative}/huge " +
                "(Unified 2.5 Huge ships map to First Edition Epic; " +
                "Epic ships are not yet implemented)",
                $"{ModelRootRelative}/bases (base infrastructure, not ship models)",
                $"{ModelRootRelative}/holo.obj (display infrastructure, not a ship model)"
            ],
            ObjFilesScanned = entries.Count,
            UsedPrimary = entries.Count(entry => entry.UsageStatus == "UsedPrimary"),
            UsedMultipart = entries.Count(entry => entry.UsageStatus == "UsedMultipart"),
            UsedConfigured = entries.Count(entry => entry.UsageStatus == "UsedConfigured"),
            UsedPipelineInput = entries.Count(entry => entry.UsageStatus == "UsedPipelineInput"),
            ReviewCandidates = entries.Count(entry => entry.UsageStatus == "ReviewCandidate"),
            DuplicateHashGroups = entries
                .Where(entry => entry.DuplicatePaths.Count > 0)
                .Select(entry => entry.Sha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            MissingConfiguredModels = missingConfigured
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MultipartErrors = multipartErrors,
            Entries = entries
                .OrderBy(entry => entry.CurrentFolderClass, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public void WriteReports(
        string outputFolder,
        ShipModelInventoryManifest manifest)
    {
        Directory.CreateDirectory(outputFolder);

        File.WriteAllText(
            Path.Combine(outputFolder, "ship-model-inventory.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));

        WriteCsv(
            Path.Combine(outputFolder, "ship-model-inventory.csv"),
            manifest);
        WriteMarkdown(
            Path.Combine(outputFolder, "SHIP-MODEL-INVENTORY.md"),
            manifest);
    }

    private static void CollectConfiguredModelUsages(
        string repositoryRoot,
        IDictionary<string, ShipModelUsage> usages,
        ISet<string> missingConfigured,
        IReadOnlySet<string> historicalObsolete)
    {
        var candidates = new[]
        {
            Path.Combine(
                repositoryRoot,
                "tools", "UnifiedToolkit", "Commands",
                "GeneratePrototypeSaveCommand.cs"),
            Path.Combine(
                repositoryRoot,
                "Commands",
                "GeneratePrototypeSaveCommand.cs")
        };

        var sourcePath = candidates.FirstOrDefault(File.Exists);
        if (sourcePath is null)
        {
            throw new FileNotFoundException(
                "GeneratePrototypeSaveCommand.cs was not found under the repository.");
        }

        var source = File.ReadAllText(sourcePath);
        foreach (var path in ExtractConcatenatedObjPaths(source))
        {
            var normalised = NormaliseModelPath(path);
            if (normalised is null)
                continue;

            if (historicalObsolete.Contains(normalised))
                continue;

            var usage = GetUsage(usages, normalised);
            usage.UsageTypes.Add(IsMultipartPath(normalised)
                ? "MultipartConfigured"
                : "Configured");
            usage.UsageSources.Add(Normalise(
                Path.GetRelativePath(repositoryRoot, sourcePath)));

            var fullPath = Path.Combine(
                repositoryRoot,
                normalised.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                missingConfigured.Add(normalised);
        }
    }

    private static IEnumerable<string> ExtractConcatenatedObjPaths(string source)
    {
        const string sequencePattern =
            "(?:\\\"(?:[^\\\"\\\\]|\\\\.)*\\\"\\s*\\+\\s*)*" +
            "\\\"(?:[^\\\"\\\\]|\\\\.)*\\.obj\\\"";

        foreach (Match sequence in Regex.Matches(
                     source,
                     sequencePattern,
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var parts = Regex.Matches(
                    sequence.Value,
                    "\\\"((?:[^\\\"\\\\]|\\\\.)*)\\\"")
                .Select(match => Regex.Unescape(match.Groups[1].Value));

            var combined = string.Concat(parts);
            if (combined.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                yield return combined;
        }
    }

    private static void CollectGeneratedSaveUsages(
        string repositoryRoot,
        IDictionary<string, ShipModelUsage> usages)
    {
        var savesFolder = Path.Combine(
            repositoryRoot,
            "assets", "generated", "validation", "saves");

        if (!Directory.Exists(savesFolder))
            return;

        foreach (var savePath in EnumerateValidationSavePaths(repositoryRoot, savesFolder))
        {
            string source;
            try
            {
                source = File.ReadAllText(savePath);
            }
            catch
            {
                continue;
            }

            var shipGroup = Path.GetFileNameWithoutExtension(savePath)
                .Replace(
                    "__all-pilots",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase);

            // The generated saves can contain very large embedded Lua and JSON
            // strings. Scanning the source text for repository OBJ paths is more
            // robust than relying solely on recursive JsonNode traversal.
            foreach (var modelPath in ExtractObjPathsFromText(source))
            {
                if (!modelPath.StartsWith(
                        ModelRootRelative + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (modelPath.Contains(
                        "/ships-v2/bases/",
                        StringComparison.OrdinalIgnoreCase)
                    || modelPath.EndsWith(
                        "/ships-v2/holo.obj",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var usage = GetUsage(usages, modelPath);
                usage.UsageTypes.Add(
                    IsMultipartPath(modelPath)
                        ? "MultipartGeneratedSave"
                        : "PrimaryGeneratedSave");
                usage.UsageSources.Add(Normalise(
                    Path.GetRelativePath(repositoryRoot, savePath)));
                usage.ShipGroups.Add(shipGroup);
            }

            // Retain structured traversal to collect First Edition base-size
            // metadata when the save can be parsed successfully.
            try
            {
                var root = JsonNode.Parse(source);
                CollectFromNode(
                    root,
                    repositoryRoot,
                    savePath,
                    shipGroup,
                    inheritedBaseSize: null,
                    usages);
            }
            catch
            {
                // The text scan above remains authoritative for model usage.
            }
        }
    }

    private static IEnumerable<string> ExtractObjPathsFromText(string source)
    {
        const string objPattern =
            @"(?i)(?:https?://[^\s\""']+)?"
            + @"assets/(?:source/unified25/assets/)?ships-v2/"
            + @"[^\s\""']+?\.obj";

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(
                     source,
                     objPattern,
                     RegexOptions.CultureInvariant))
        {
            var normalised = NormaliseModelPath(match.Value);
            if (normalised is not null)
                paths.Add(normalised);
        }

        return paths;
    }

    private static IEnumerable<string> EnumerateValidationSavePaths(
        string repositoryRoot,
        string savesFolder)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(
                     savesFolder,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Equals(
                    "ship-validation-saves.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(Path.GetFullPath(path));
        }

        var manifestPath = Path.Combine(
            repositoryRoot,
            "assets", "generated", "validation", "ship-validation-saves.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
                CollectJsonFilePaths(manifest, repositoryRoot, paths);
            }
            catch
            {
                // Direct save enumeration remains authoritative if the manifest is stale.
            }
        }

        return paths
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static void CollectJsonFilePaths(
        JsonNode? node,
        string repositoryRoot,
        ISet<string> paths)
    {
        if (node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && text.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = text.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(candidate))
                candidate = Path.Combine(repositoryRoot, candidate);

            candidate = Path.GetFullPath(candidate);
            if (File.Exists(candidate)
                && candidate.Contains(
                    $"{Path.DirectorySeparatorChar}validation{Path.DirectorySeparatorChar}saves{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(candidate);
            }

            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
                CollectJsonFilePaths(child, repositoryRoot, paths);
            return;
        }

        if (node is JsonObject obj)
        {
            foreach (var property in obj)
                CollectJsonFilePaths(property.Value, repositoryRoot, paths);
        }
    }

    private static HashSet<string> LoadHistoricalObsoleteModelPaths(
        string repositoryRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reportPaths = new[]
        {
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports", "model-selection",
                "ship-model-selection-audit.json"),
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports", "model-cleanup",
                "obsolete-model-cleanup.json")
        };

        foreach (var reportPath in reportPaths.Where(File.Exists))
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(reportPath));
                CollectHistoricalModelPaths(root, paths);
            }
            catch
            {
                // A malformed historical report must not prevent inventory generation.
            }
        }

        return paths;
    }

    private static void CollectHistoricalModelPaths(
        JsonNode? node,
        ISet<string> paths)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array)
                CollectHistoricalModelPaths(child, paths);
            return;
        }

        if (node is not JsonObject obj)
            return;

        foreach (var propertyName in new[]
                 {
                     "rejectedModelPath",
                     "originalPath",
                     "originalRepositoryPath"
                 })
        {
            var value = obj[propertyName]?.GetValue<string>();
            var normalised = value is null ? null : NormaliseModelPath(value);
            if (normalised is not null)
                paths.Add(normalised);
        }

        foreach (var property in obj)
            CollectHistoricalModelPaths(property.Value, paths);
    }

    private static void CollectFromNode(
        JsonNode? node,
        string repositoryRoot,
        string savePath,
        string shipGroup,
        string? inheritedBaseSize,
        IDictionary<string, ShipModelUsage> usages)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                CollectFromNode(
                    child,
                    repositoryRoot,
                    savePath,
                    shipGroup,
                    inheritedBaseSize,
                    usages);
            }
            return;
        }

        if (node is not JsonObject obj)
            return;

        var baseSize = TryReadBaseSize(obj) ?? inheritedBaseSize;
        if (obj["CustomMesh"] is JsonObject mesh)
        {
            var meshUrl = mesh["MeshURL"]?.GetValue<string>() ?? string.Empty;
            var modelPath = NormaliseModelPath(meshUrl);
            if (modelPath is not null)
            {
                var usage = GetUsage(usages, modelPath);
                var nickname = obj["Nickname"]?.GetValue<string>() ?? string.Empty;
                usage.UsageTypes.Add(
                    nickname.Equals("Config", StringComparison.OrdinalIgnoreCase)
                    || IsMultipartPath(modelPath)
                        ? "MultipartGeneratedSave"
                        : "PrimaryGeneratedSave");
                usage.UsageSources.Add(Normalise(
                    Path.GetRelativePath(repositoryRoot, savePath)));
                usage.ShipGroups.Add(shipGroup);
                if (!string.IsNullOrWhiteSpace(baseSize))
                {
                    usage.FirstEditionBaseSizes.Add(
                        FirstEditionShipSizeTerminology.ToFirstEditionTerm(baseSize));
                }
            }
        }

        foreach (var property in obj)
        {
            CollectFromNode(
                property.Value,
                repositoryRoot,
                savePath,
                shipGroup,
                baseSize,
                usages);
        }
    }

    private static string? TryReadBaseSize(JsonObject obj)
    {
        var gmNotes = obj["GMNotes"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(gmNotes))
            return null;

        try
        {
            var state = JsonNode.Parse(gmNotes)?.AsObject();
            var sourceSize = state?["shipData"]?["Size"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(sourceSize)
                ? null
                : FirstEditionShipSizeTerminology.ToFirstEditionTerm(sourceSize);
        }
        catch
        {
            return null;
        }
    }

    private static void CollectPipelineInputUsages(
        string repositoryRoot,
        IDictionary<string, ShipModelUsage> usages)
    {
        // assets/manifests/assets.json is a catalogue of imported files,
        // not an active model selection source. Counting every catalogue entry
        // as a pipeline dependency prevents legitimate obsolete-model review.
        var explicitFiles = new[]
        {
            Path.Combine(
                repositoryRoot,
                "ukb", "ship-links.json")
        };

        foreach (var path in explicitFiles.Where(File.Exists))
            CollectPipelineInputFile(repositoryRoot, path, usages);

        var recursiveFolders = new[]
        {
            Path.Combine(
                repositoryRoot,
                "assets", "generated", "validation", "plans"),
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports", "phase11",
                "ship-package-planning"),
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports", "phase12b")
        };

        foreach (var folder in recursiveFolders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(
                         folder,
                         "*.json",
                         SearchOption.AllDirectories))
            {
                CollectPipelineInputFile(repositoryRoot, path, usages);
            }
        }
    }

    private static void CollectPipelineInputFile(
        string repositoryRoot,
        string sourcePath,
        IDictionary<string, ShipModelUsage> usages)
    {
        string source;
        try
        {
            source = File.ReadAllText(sourcePath);
        }
        catch
        {
            return;
        }

        foreach (var modelPath in ExtractObjPathsFromText(source))
        {
            if (!modelPath.StartsWith(
                    ModelRootRelative + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (modelPath.Contains(
                    "/ships-v2/bases/",
                    StringComparison.OrdinalIgnoreCase)
                || modelPath.EndsWith(
                    "/ships-v2/holo.obj",
                    StringComparison.OrdinalIgnoreCase)
                || modelPath.Contains(
                    "/ships-v2/huge/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var usage = GetUsage(usages, modelPath);
            usage.UsageTypes.Add("PipelineInput");
            usage.UsageSources.Add(Normalise(
                Path.GetRelativePath(repositoryRoot, sourcePath)));
        }
    }

    private static void ProtectProductionModels(
        IDictionary<string, ShipModelUsage> usages)
    {
        foreach (var path in ProtectedProductionModels)
        {
            var usage = GetUsage(usages, path);
            usage.UsageTypes.Add("ProtectedProductionModel");
            usage.UsageSources.Add("Built-in production-model safety contract");
        }
    }

    private static void ProtectMultipartSets(
        IDictionary<string, ShipModelUsage> usages)
    {
        foreach (var set in MultipartSets)
        {
            foreach (var path in set.Value)
            {
                var usage = GetUsage(usages, path);
                usage.UsageTypes.Add("MultipartRequired");
                usage.UsageSources.Add("Built-in multipart safety contract");
                usage.ShipGroups.Add(set.Key);
            }
        }
    }

    private static ShipModelInventoryEntry BuildEntry(
        string repositoryRoot,
        string fullPath,
        IReadOnlyDictionary<string, ShipModelUsage> usages)
    {
        var relative = Normalise(Path.GetRelativePath(repositoryRoot, fullPath));
        usages.TryGetValue(relative, out var usage);
        usage ??= new ShipModelUsage();

        var multipartSet = MultipartSets
            .FirstOrDefault(set => set.Value.Contains(
                relative,
                StringComparer.OrdinalIgnoreCase));
        var isMultipart = multipartSet.Value is not null;

        var status = DetermineStatus(usage, isMultipart);
        var currentClass = DetermineCurrentClass(relative);
        var baseSizes = usage.FirstEditionBaseSizes
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShipModelInventoryEntry
        {
            RepositoryPath = relative,
            FileName = Path.GetFileName(fullPath),
            CurrentFolderClass = currentClass,
            FirstEditionBaseSizes = baseSizes,
            RecommendedUnified1eFolder = DetermineRecommendedFolder(
                relative,
                currentClass,
                baseSizes),
            SizeBytes = new FileInfo(fullPath).Length,
            Sha256 = CalculateSha256(fullPath),
            UsageStatus = status,
            RecommendedAction = status == "ReviewCandidate"
                ? "Review before quarantine or removal"
                : "Retain",
            UsageTypes = usage.UsageTypes
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UsageSources = usage.UsageSources
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ShipGroups = usage.ShipGroups
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IsMultipartMember = isMultipart,
            MultipartSet = multipartSet.Key ?? string.Empty
        };
    }

    private static string DetermineStatus(
        ShipModelUsage usage,
        bool isMultipart)
    {
        if (isMultipart
            || usage.UsageTypes.Any(type => type.Contains(
                "Multipart",
                StringComparison.OrdinalIgnoreCase)))
        {
            return "UsedMultipart";
        }

        if (usage.UsageTypes.Contains("PrimaryGeneratedSave"))
            return "UsedPrimary";

        if (usage.UsageTypes.Contains("PipelineInput"))
            return "UsedPipelineInput";

        if (usage.UsageTypes.Count > 0)
            return "UsedConfigured";

        return "ReviewCandidate";
    }

    private static void ApplyDuplicateInformation(
        IReadOnlyList<ShipModelInventoryEntry> entries)
    {
        foreach (var group in entries
                     .GroupBy(entry => entry.Sha256, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var paths = group
                .Select(entry => entry.RepositoryPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var entry in group)
            {
                entry.DuplicatePaths = paths
                    .Where(path => !path.Equals(
                        entry.RepositoryPath,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
    }

    private static List<string> ValidateMultipartSets(
        string repositoryRoot,
        IReadOnlyList<ShipModelInventoryEntry> entries)
    {
        var byPath = entries.ToDictionary(
            entry => entry.RepositoryPath,
            StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var set in MultipartSets)
        {
            foreach (var path in set.Value)
            {
                var fullPath = Path.Combine(
                    repositoryRoot,
                    path.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath))
                {
                    errors.Add($"{set.Key}: required multipart OBJ is missing: {path}");
                    continue;
                }

                if (!byPath.TryGetValue(path, out var entry)
                    || entry.UsageStatus != "UsedMultipart")
                {
                    errors.Add(
                        $"{set.Key}: required multipart OBJ was not protected as UsedMultipart: {path}");
                }
            }
        }

        return errors;
    }

    private static ShipModelUsage GetUsage(
        IDictionary<string, ShipModelUsage> usages,
        string path)
    {
        path = Normalise(path);
        if (!usages.TryGetValue(path, out var usage))
        {
            usage = new ShipModelUsage();
            usages[path] = usage;
        }

        return usage;
    }

    private static string? NormaliseModelPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalised = Uri.UnescapeDataString(value).Replace('\\', '/');
        var markerIndex = normalised.IndexOf(
            ModelRootRelative + "/",
            StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            const string publicMarker = "assets/ships-v2/";
            markerIndex = normalised.IndexOf(
                publicMarker,
                StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return null;

            normalised = ModelRootRelative + "/"
                + normalised[(markerIndex + publicMarker.Length)..];
        }
        else
        {
            normalised = normalised[markerIndex..];
        }

        var queryIndex = normalised.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            normalised = normalised[..queryIndex];

        return normalised.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
            ? Normalise(normalised)
            : null;
    }

    private static bool IsMultipartPath(string path)
    {
        return MultipartSets.Values
            .SelectMany(paths => paths)
            .Contains(path, StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains("open", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains("closed", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetermineCurrentClass(string path)
    {
        foreach (var folder in new[] { "small", "medium", "large", "huge" })
        {
            if (path.Contains($"/ships-v2/{folder}/", StringComparison.OrdinalIgnoreCase))
                return folder.Equals("huge", StringComparison.OrdinalIgnoreCase)
                    ? FirstEditionShipSizeTerminology.Unified25HugeFolder
                    : folder;
        }

        return "unknown";
    }

    private static string DetermineRecommendedFolder(
        string path,
        string currentClass,
        IReadOnlyList<string> baseSizes)
    {
        var shipFolder = Path.GetFileName(
            Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar))
            ?? string.Empty);
        var firstEditionClass = baseSizes.Count == 1
            ? FirstEditionShipSizeTerminology.ToFirstEditionTerm(baseSizes[0])
            : FirstEditionShipSizeTerminology.ToFirstEditionTerm(currentClass);

        if (firstEditionClass.Equals("medium", StringComparison.OrdinalIgnoreCase)
            || firstEditionClass.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Review First Edition base size before unified1e migration";
        }

        return $"assets/source/unified1e/ships/{firstEditionClass}/{shipFolder}";
    }

    private static string CalculateSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteCsv(
        string path,
        ShipModelInventoryManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "RepositoryPath,FileName,CurrentFolderClass,FirstEditionBaseSizes," +
            "RecommendedUnified1eFolder,SizeBytes,Sha256,UsageStatus," +
            "RecommendedAction,UsageTypes,ShipGroups,MultipartSet,DuplicatePaths");

        foreach (var entry in manifest.Entries)
        {
            writer.WriteLine(string.Join(",",
                Csv(entry.RepositoryPath),
                Csv(entry.FileName),
                Csv(entry.CurrentFolderClass),
                Csv(string.Join(";", entry.FirstEditionBaseSizes)),
                Csv(entry.RecommendedUnified1eFolder),
                entry.SizeBytes,
                Csv(entry.Sha256),
                Csv(entry.UsageStatus),
                Csv(entry.RecommendedAction),
                Csv(string.Join(";", entry.UsageTypes)),
                Csv(string.Join(";", entry.ShipGroups)),
                Csv(entry.MultipartSet),
                Csv(string.Join(";", entry.DuplicatePaths))));
        }
    }

    private static void WriteMarkdown(
        string path,
        ShipModelInventoryManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Ship Model Inventory");
        writer.WriteLine();
        writer.WriteLine("## Scan scope");
        writer.WriteLine();
        writer.WriteLine("Included:");
        foreach (var folder in manifest.IncludedFolders)
            writer.WriteLine($"- `{folder}`");
        writer.WriteLine();
        writer.WriteLine("Excluded:");
        foreach (var folder in manifest.ExcludedFolders)
            writer.WriteLine($"- `{folder}`");
        writer.WriteLine();
        writer.WriteLine("No files were moved or deleted by this audit.");
        writer.WriteLine();
        writer.WriteLine("## Summary");
        writer.WriteLine();
        writer.WriteLine($"- OBJ files scanned: {manifest.ObjFilesScanned}");
        writer.WriteLine($"- Used primary: {manifest.UsedPrimary}");
        writer.WriteLine($"- Used multipart: {manifest.UsedMultipart}");
        writer.WriteLine($"- Used configured: {manifest.UsedConfigured}");
        writer.WriteLine($"- Used pipeline input: {manifest.UsedPipelineInput}");
        writer.WriteLine($"- Review candidates: {manifest.ReviewCandidates}");
        writer.WriteLine($"- Duplicate hash groups: {manifest.DuplicateHashGroups}");
        writer.WriteLine();

        if (manifest.MultipartErrors.Count > 0)
        {
            writer.WriteLine("## Multipart protection errors");
            writer.WriteLine();
            foreach (var error in manifest.MultipartErrors)
                writer.WriteLine($"- {error}");
            writer.WriteLine();
        }

        writer.WriteLine("## Review candidates");
        writer.WriteLine();
        writer.WriteLine("| Current folder | OBJ | SHA-256 | Duplicate paths | Recommended action |");
        writer.WriteLine("|---|---|---|---|---|");
        foreach (var entry in manifest.Entries
                     .Where(entry => entry.UsageStatus == "ReviewCandidate"))
        {
            writer.WriteLine(
                $"| {entry.CurrentFolderClass} | `{entry.RepositoryPath}` | " +
                $"`{entry.Sha256}` | {MarkdownList(entry.DuplicatePaths)} | " +
                $"{entry.RecommendedAction} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Used models");
        writer.WriteLine();
        writer.WriteLine("| Status | OBJ | Ship groups | First Edition size | Multipart set |");
        writer.WriteLine("|---|---|---|---|---|");
        foreach (var entry in manifest.Entries
                     .Where(entry => entry.UsageStatus != "ReviewCandidate"))
        {
            writer.WriteLine(
                $"| {entry.UsageStatus} | `{entry.RepositoryPath}` | " +
                $"{MarkdownList(entry.ShipGroups)} | " +
                $"{MarkdownList(entry.FirstEditionBaseSizes)} | " +
                $"{entry.MultipartSet} |");
        }
    }

    private static string MarkdownList(IReadOnlyCollection<string> values) =>
        values.Count == 0
            ? string.Empty
            : string.Join("<br>", values.Select(value => $"`{value}`"));

    private static string Csv(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string Normalise(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
