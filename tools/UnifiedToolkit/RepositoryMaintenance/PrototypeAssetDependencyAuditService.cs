using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class PrototypeAssetDependencyAuditService
{
    private static readonly HashSet<string> AssetPropertyNames = new(
        [
            "MeshURL", "DiffuseURL", "NormalURL", "ColliderURL",
            "ImageURL", "ImageSecondaryURL", "URL", "TableURL",
            "SkyURL", "AssetBundle", "AssetbundleURL",
            "AssetbundleSecondaryURL", "CloudURL"
        ],
        StringComparer.OrdinalIgnoreCase);

    public PrototypeAssetDependencyAudit Run(
        string repositoryRoot,
        string referenceSavePath)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        referenceSavePath = Path.GetFullPath(referenceSavePath);

        var inputs = DiscoverInputFiles(repositoryRoot, referenceSavePath);
        var migrationMappings = LoadMigrationMappings(repositoryRoot);
        var found = new Dictionary<string, MutableDependency>(
            StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var input in inputs)
        {
            var relativeSource = Normalise(
                Path.GetRelativePath(repositoryRoot, input.Path));

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(input.Path));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                warnings.Add($"Could not parse {relativeSource}: {ex.Message}");
                continue;
            }

            if (root is null)
                continue;

            if (input.Kind == InputKind.ReferenceSave
                && root is JsonObject referenceRoot)
            {
                CollectReferenceEnvironment(
                    referenceRoot,
                    relativeSource,
                    found);
            }
            else
            {
                CollectStructured(
                    root,
                    relativeSource,
                    input.Kind,
                    string.Empty,
                    found);
            }
        }

        var entries = found.Values
            .Select(value => Classify(
                repositoryRoot,
                value,
                migrationMappings))
            .OrderBy(entry => entry.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.AssetKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.NormalizedReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PrototypeAssetDependencyAudit
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = Normalise(repositoryRoot),
            ReferenceSave = Normalise(referenceSavePath),
            FilesScanned = inputs.Count,
            ReferencesFound = entries.Sum(entry => entry.Occurrences),
            UniqueDependencies = entries.Count,
            AlreadyMigrated = Count(entries, "AlreadyMigrated"),
            Unified25Dependencies = Count(entries, "Unified25Dependency"),
            RepositoryDependencies = Count(entries, "RepositoryDependency"),
            UpstreamDependencies = Count(entries, "UpstreamDependency"),
            ExternalDependencies = Count(entries, "ExternalDependency"),
            EnvironmentDependencies = entries.Count(entry => entry.Scope == "Environment"),
            RuntimeDependencies = entries.Count(entry => entry.Scope == "Runtime"),
            ShipDependencies = entries.Count(entry => entry.Scope == "Ship"),
            SupportingDependencies = entries.Count(
                entry => entry.Scope == "SupportingAsset"),
            MissingRepositoryFiles = entries.Count(entry =>
                entry.RepositoryPath.Length > 0 && !entry.RepositoryFileExists),
            Entries = entries,
            ScanWarnings = warnings
        };
    }

    public static void WriteReports(
        string outputFolder,
        PrototypeAssetDependencyAudit audit)
    {
        Directory.CreateDirectory(outputFolder);
        var jsonPath = Path.Combine(outputFolder, "prototype-asset-dependencies.json");
        var csvPath = Path.Combine(outputFolder, "prototype-asset-dependencies.csv");
        var markdownPath = Path.Combine(outputFolder, "PROTOTYPE-ASSET-DEPENDENCIES.md");

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(audit, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            new UTF8Encoding(false));

        using (var writer = new StreamWriter(csvPath, false, new UTF8Encoding(false)))
        {
            writer.WriteLine(
                "Scope,Category,AssetKind,Reference,RepositoryPath," +
                "RepositoryFileExists,MigrationEquivalentPath," +
                "MigrationEquivalentExists,RecommendedAction,SuggestedDestination," +
                "Occurrences,Sources,JsonProperties");

            foreach (var entry in audit.Entries)
            {
                writer.WriteLine(string.Join(",",
                    Csv(entry.Scope), Csv(entry.Category), Csv(entry.AssetKind),
                    Csv(entry.Reference), Csv(entry.RepositoryPath),
                    entry.RepositoryFileExists,
                    Csv(entry.MigrationEquivalentPath),
                    entry.MigrationEquivalentExists,
                    Csv(entry.RecommendedAction),
                    Csv(entry.SuggestedDestination), entry.Occurrences,
                    Csv(string.Join("; ", entry.Sources)),
                    Csv(string.Join("; ", entry.JsonProperties))));
            }
        }

        using var md = new StreamWriter(markdownPath, false, new UTF8Encoding(false));
        md.WriteLine("# Effective Prototype Asset Dependency Audit");
        md.WriteLine();
        md.WriteLine("This report includes only structured asset fields from generated prototypes, runtime templates, and top-level reference-save environment settings.");
        md.WriteLine();
        md.WriteLine($"- Files scanned: {audit.FilesScanned}");
        md.WriteLine($"- Unique dependencies: {audit.UniqueDependencies}");
        md.WriteLine($"- Environment dependencies: {audit.EnvironmentDependencies}");
        md.WriteLine($"- Runtime dependencies: {audit.RuntimeDependencies}");
        md.WriteLine($"- Ship dependencies: {audit.ShipDependencies}");
        md.WriteLine($"- Supporting dependencies: {audit.SupportingDependencies}");
        md.WriteLine($"- Already migrated: {audit.AlreadyMigrated}");
        md.WriteLine($"- Unified 2.5 dependencies: {audit.Unified25Dependencies}");
        md.WriteLine($"- Repository dependencies: {audit.RepositoryDependencies}");
        md.WriteLine($"- Upstream dependencies: {audit.UpstreamDependencies}");
        md.WriteLine($"- External dependencies: {audit.ExternalDependencies}");
        md.WriteLine($"- Missing repository files: {audit.MissingRepositoryFiles}");
        md.WriteLine();
        md.WriteLine("| Scope | Category | Kind | Reference | Action | Suggested destination |");
        md.WriteLine("|---|---|---|---|---|---|");
        foreach (var entry in audit.Entries)
        {
            md.WriteLine(
                $"| {EscapeMd(entry.Scope)} | {EscapeMd(entry.Category)} | " +
                $"{EscapeMd(entry.AssetKind)} | `{EscapeMd(entry.NormalizedReference)}` | " +
                $"{EscapeMd(entry.RecommendedAction)} | " +
                $"`{EscapeMd(entry.SuggestedDestination)}` |");
        }
    }

    private static int Count(
        IEnumerable<PrototypeAssetDependencyEntry> entries,
        string category) =>
        entries.Count(entry => entry.Category == category);

    private static List<InputFile> DiscoverInputFiles(
        string repositoryRoot,
        string referenceSavePath)
    {
        var files = new Dictionary<string, InputKind>(
            StringComparer.OrdinalIgnoreCase);

        if (File.Exists(referenceSavePath))
            files[referenceSavePath] = InputKind.ReferenceSave;

        AddJsonFolder(
            files,
            Path.Combine(repositoryRoot, "assets", "generated", "validation", "saves"),
            InputKind.GeneratedPrototype);

        AddJsonFolder(
            files,
            Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports", "phase12b", "runtime-template-extraction"),
            InputKind.RuntimeTemplate);

        return files
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new InputFile(pair.Key, pair.Value))
            .ToList();
    }

    private static void AddJsonFolder(
        IDictionary<string, InputKind> files,
        string folder,
        InputKind kind)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     folder,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            files[Path.GetFullPath(file)] = kind;
        }
    }

    private static void CollectReferenceEnvironment(
        JsonObject root,
        string source,
        IDictionary<string, MutableDependency> found)
    {
        foreach (var property in root)
        {
            if (property.Key.Equals("ObjectStates", StringComparison.OrdinalIgnoreCase)
                || property.Key.Equals("LuaScript", StringComparison.OrdinalIgnoreCase)
                || property.Key.Equals("LuaScriptState", StringComparison.OrdinalIgnoreCase)
                || property.Key.Equals("XmlUI", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CollectStructured(
                property.Value,
                source,
                InputKind.ReferenceSave,
                property.Key,
                found);
        }
    }

    private static void CollectStructured(
        JsonNode? node,
        string source,
        InputKind inputKind,
        string propertyPath,
        IDictionary<string, MutableDependency> found)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var path = propertyPath.Length == 0
                    ? property.Key
                    : propertyPath + "." + property.Key;

                if (property.Value is JsonValue value
                    && value.TryGetValue<string>(out var stringValue)
                    && !string.IsNullOrWhiteSpace(stringValue)
                    && AssetPropertyNames.Contains(property.Key))
                {
                    AddReference(
                        stringValue,
                        source,
                        inputKind,
                        path,
                        property.Key,
                        found);
                }
                else if (property.Value is not JsonValue)
                {
                    CollectStructured(
                        property.Value,
                        source,
                        inputKind,
                        path,
                        found);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                CollectStructured(
                    array[index],
                    source,
                    inputKind,
                    $"{propertyPath}[{index}]",
                    found);
            }
        }
    }

    private static void AddReference(
        string raw,
        string source,
        InputKind inputKind,
        string propertyPath,
        string propertyName,
        IDictionary<string, MutableDependency> found)
    {
        var normalized = NormalizeReference(raw);
        if (normalized.Length == 0
            || normalized.Equals("null", StringComparison.OrdinalIgnoreCase)
            || !LooksLikeAsset(normalized))
        {
            return;
        }

        var repositoryPath = ResolveRepositoryPath(normalized);
        var key = repositoryPath.Length > 0
            ? "repo:" + repositoryPath
            : "url:" + normalized;

        if (!found.TryGetValue(key, out var item))
        {
            item = new MutableDependency
            {
                Original = raw,
                Normalized = normalized,
                RepositoryPath = repositoryPath
            };
            found[key] = item;
        }

        item.Occurrences++;
        item.Sources.Add(source);
        item.Properties.Add(propertyPath);
        item.PropertyNames.Add(propertyName);
        item.InputKinds.Add(inputKind);
    }

    private static PrototypeAssetDependencyEntry Classify(
        string repositoryRoot,
        MutableDependency item,
        IReadOnlyList<MigrationMapping> migrationMappings)
    {
        var reference = item.Normalized;
        var repositoryPath = item.RepositoryPath;
        var host = Uri.TryCreate(reference, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;

        var migrationEquivalent = ResolveMigrationEquivalent(
            repositoryPath,
            migrationMappings);
        var migrationEquivalentExists =
            migrationEquivalent.Length > 0
            && File.Exists(Path.Combine(
                repositoryRoot,
                migrationEquivalent.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        var category = DetermineCategory(
            reference,
            repositoryPath,
            host,
            migrationEquivalentExists);
        var kind = DetermineAssetKind(
            reference,
            repositoryPath,
            item.PropertyNames);
        var scope = DetermineScope(repositoryPath, kind);
        var exists = repositoryPath.Length > 0 && File.Exists(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
        var action = DetermineAction(category, scope, kind);
        var destination = migrationEquivalent.Length > 0
            ? migrationEquivalent
            : SuggestDestination(
                category,
                scope,
                kind,
                repositoryPath,
                reference);

        return new PrototypeAssetDependencyEntry
        {
            Reference = item.Original,
            NormalizedReference = reference,
            Category = category,
            Scope = scope,
            AssetKind = kind,
            Host = host,
            RepositoryPath = repositoryPath,
            RepositoryFileExists = exists,
            MigrationEquivalentPath = migrationEquivalent,
            MigrationEquivalentExists = migrationEquivalentExists,
            RecommendedAction = action,
            SuggestedDestination = destination,
            Sources = item.Sources.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            JsonProperties = item.Properties.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Occurrences = item.Occurrences
        };
    }

    private static string DetermineCategory(
        string reference,
        string repositoryPath,
        string host,
        bool migrationEquivalentExists)
    {
        if (repositoryPath.StartsWith(
                "assets/source/unified1e/",
                StringComparison.OrdinalIgnoreCase)
            || migrationEquivalentExists)
        {
            return "AlreadyMigrated";
        }

        if (repositoryPath.StartsWith(
                "assets/source/unified25/",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Unified25Dependency";
        }

        if (repositoryPath.Length > 0)
            return "RepositoryDependency";

        if (host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            && reference.Contains(
                "/JohnnyCheese/TTS_X-Wing2.0/",
                StringComparison.OrdinalIgnoreCase))
        {
            return "UpstreamDependency";
        }

        return "ExternalDependency";
    }

    private static string DetermineScope(
        string repositoryPath,
        string kind)
    {
        if (kind is "Skybox" or "Table" or "Mat")
            return "Environment";

        if (repositoryPath.Contains(
                "/ships/",
                StringComparison.OrdinalIgnoreCase)
            || repositoryPath.Contains(
                "/ships-v2/",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Ship";
        }

        if (kind is "Collider"
            or "Token"
            or "AssetBundle"
            or "UIOrRuntimeAsset")
        {
            return "Runtime";
        }

        return "SupportingAsset";
    }

    private static string DetermineAssetKind(
        string reference,
        string repositoryPath,
        IEnumerable<string> propertyNames)
    {
        var joined = string.Join(" ", propertyNames);
        var lower = (repositoryPath.Length > 0 ? repositoryPath : reference)
            .ToLowerInvariant();

        if (joined.Contains("SkyURL", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("skybox")
            || lower.EndsWith("/sky.jpg")) return "Skybox";
        if (joined.Contains("TableURL", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("table")) return "Table";
        if (lower.Contains("playmat") || lower.Contains("basicmat")
            || lower.Contains("/mat")) return "Mat";
        if (lower.Contains("collider")) return "Collider";
        if (lower.Contains("token") || lower.Contains("/config/")) return "Token";
        if (lower.EndsWith(".unity3d")) return "AssetBundle";
        if (lower.EndsWith(".obj")) return "Model";
        if (lower.EndsWith(".png") || lower.EndsWith(".jpg")
            || lower.EndsWith(".jpeg") || lower.EndsWith(".webp")) return "Image";
        if (joined.Contains("URL", StringComparison.OrdinalIgnoreCase)) return "UIOrRuntimeAsset";
        return "Other";
    }

    private static string DetermineAction(
        string category,
        string scope,
        string kind) => category switch
    {
        "AlreadyMigrated" => "Retain unified1e reference",
        "Unified25Dependency" => scope switch
        {
            "Environment" => "Migrate environment asset before Phase 13C",
            "Runtime" => "Migrate runtime dependency before Phase 13C",
            "Ship" => "Rewire to existing unified1e ship asset",
            _ => "Review for unified1e consolidation"
        },
        "RepositoryDependency" => "Review repository-owned supporting asset",
        "UpstreamDependency" => "Import repository-owned copy and rewire",
        "ExternalDependency" when kind is "Skybox" or "Table" or "Mat" =>
            "Review external environment dependency",
        _ => "Manual review"
    };

    private static string SuggestDestination(
        string category,
        string scope,
        string kind,
        string repositoryPath,
        string reference)
    {
        if (category == "AlreadyMigrated")
            return repositoryPath;

        var filename = Path.GetFileName(
            repositoryPath.Length > 0
                ? repositoryPath
                : Uri.TryCreate(
                    reference,
                    UriKind.Absolute,
                    out var uri)
                    ? uri.AbsolutePath
                    : reference);
        if (filename.Length == 0)
            return string.Empty;

        var importedRelative = repositoryPath.StartsWith(
                "assets/source/unified25/assets/",
                StringComparison.OrdinalIgnoreCase)
            ? repositoryPath[
                "assets/source/unified25/assets/".Length..]
            : filename;

        return (scope, kind) switch
        {
            ("Environment", "Skybox") =>
                $"assets/source/unified1e/environment/skyboxes/{filename}",
            ("Environment", "Table") =>
                $"assets/source/unified1e/environment/tables/{filename}",
            ("Environment", "Mat") =>
                $"assets/source/unified1e/environment/mats/{filename}",
            ("Runtime", "Collider") =>
                $"assets/source/unified1e/runtime/colliders/{filename}",
            ("Runtime", "Token") =>
                $"assets/source/unified1e/runtime/tokens/{filename}",
            ("Runtime", "AssetBundle") =>
                $"assets/source/unified1e/runtime/bundles/{filename}",
            ("Runtime", _) =>
                $"assets/source/unified1e/runtime/assets/{importedRelative}",
            ("SupportingAsset", _) =>
                $"assets/source/unified1e/support/{importedRelative}",
            (_, "Model") =>
                $"assets/source/unified1e/support/models/{filename}",
            (_, "Image") =>
                $"assets/source/unified1e/support/images/{filename}",
            _ => string.Empty
        };
    }

    private static List<MigrationMapping> LoadMigrationMappings(
        string repositoryRoot)
    {
        var planPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase13",
            "unified1e-asset-migration",
            "unified1e-asset-migration-plan.json");

        if (!File.Exists(planPath))
            return [];

        using var document = JsonDocument.Parse(
            File.ReadAllText(planPath));

        if (!document.RootElement.TryGetProperty(
                "entries",
                out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var mappings = new List<MigrationMapping>();

        foreach (var entry in entries.EnumerateArray())
        {
            var status = entry.TryGetProperty(
                    "status",
                    out var statusElement)
                ? statusElement.GetString() ?? string.Empty
                : string.Empty;

            if (!status.Equals(
                    "Ready",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = entry.TryGetProperty(
                    "sourcePath",
                    out var sourceElement)
                ? Normalise(sourceElement.GetString() ?? string.Empty)
                : string.Empty;
            var destination = entry.TryGetProperty(
                    "destinationPath",
                    out var destinationElement)
                ? Normalise(destinationElement.GetString() ?? string.Empty)
                : string.Empty;

            if (source.Length == 0 || destination.Length == 0)
                continue;

            mappings.Add(new MigrationMapping(source, destination));
        }

        return mappings
            .OrderByDescending(mapping => mapping.SourcePath.Length)
            .ToList();
    }

    private static string ResolveMigrationEquivalent(
        string repositoryPath,
        IReadOnlyList<MigrationMapping> mappings)
    {
        if (repositoryPath.Length == 0)
            return string.Empty;

        foreach (var mapping in mappings)
        {
            if (repositoryPath.Equals(
                    mapping.SourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return mapping.DestinationPath;
            }

            var prefix = mapping.SourcePath.TrimEnd('/') + "/";
            if (!repositoryPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return mapping.DestinationPath.TrimEnd('/')
                + "/"
                + repositoryPath[prefix.Length..];
        }

        return string.Empty;
    }

    private static string ResolveRepositoryPath(string reference)
    {
        if (IsRepositoryRelative(reference))
            return reference.TrimStart('/');

        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri))
            return string.Empty;

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');

        if (uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            const string ownPrefix = "grimsdalee/xwing-unified-1e/main/";
            var ownIndex = path.IndexOf(ownPrefix, StringComparison.OrdinalIgnoreCase);
            if (ownIndex >= 0)
                return path[(ownIndex + ownPrefix.Length)..];

            const string upstreamPrefix = "JohnnyCheese/TTS_X-Wing2.0/master/";
            var upstreamIndex = path.IndexOf(
                upstreamPrefix,
                StringComparison.OrdinalIgnoreCase);
            if (upstreamIndex >= 0)
            {
                var upstreamPath = path[(upstreamIndex + upstreamPrefix.Length)..];
                return "assets/source/unified25/" + upstreamPath;
            }
        }

        return string.Empty;
    }

    private static bool IsRepositoryRelative(string reference) =>
        reference.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("source/", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("output/", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("ukb/", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith(
            "_unifiedtoolkit_reports/",
            StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAsset(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.StartsWith("http://") || lower.StartsWith("https://")
            || IsRepositoryRelative(value)
            || lower.EndsWith(".obj") || lower.EndsWith(".png")
            || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")
            || lower.EndsWith(".webp") || lower.EndsWith(".unity3d");
    }

    private static string NormalizeReference(string raw)
    {
        var value = Clean(raw).Replace("\\/", "/", StringComparison.Ordinal);

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };
            value = builder.Uri.ToString();
        }
        else
        {
            var queryIndex = value.IndexOfAny(['?', '#']);
            if (queryIndex >= 0)
                value = value[..queryIndex];
        }

        return Uri.UnescapeDataString(value).Trim();
    }

    private static string Clean(string value) =>
        value.Trim().Trim('"', '\'', ',', ';', ')', ']', '}');

    private static string Normalise(string path) => path.Replace('\\', '/');
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string EscapeMd(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private enum InputKind
    {
        ReferenceSave,
        GeneratedPrototype,
        RuntimeTemplate
    }

    private sealed record InputFile(string Path, InputKind Kind);

    private sealed record MigrationMapping(
        string SourcePath,
        string DestinationPath);

    private sealed class MutableDependency
    {
        public string Original { get; set; } = string.Empty;
        public string Normalized { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = string.Empty;
        public int Occurrences { get; set; }
        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PropertyNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<InputKind> InputKinds { get; } = [];
    }
}
