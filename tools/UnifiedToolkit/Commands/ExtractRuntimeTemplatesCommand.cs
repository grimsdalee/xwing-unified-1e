using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12B-2B:
/// Extracts exact reusable TTS runtime templates using authoritative mesh
/// identities rather than heuristic object scoring.
///
/// Save-backed templates:
///   - FirstEditionSmallShipBase
///   - FirstEditionLargeShipBase
///   - FirstEditionAssignedDial
///   - FirstEditionUnassignedDial (when present)
///
/// Peg templates are registered from the authoritative Phase 12B-2A OBJ
/// catalogue. If matching peg objects exist in the save, their complete object
/// snapshots are also retained; otherwise the serializer will construct the
/// peg Custom_Model from the registered OBJ asset.
/// </summary>
public static class ExtractRuntimeTemplatesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TemplateRule[] SaveRules =
    {
        new(
            "FirstEditionSmallShipBase",
            "SmallShipBase",
            "/assets/ships-v2/bases/small/base.obj",
            TemplateSelectionMode.UniqueMesh),
        new(
            "FirstEditionLargeShipBase",
            "LargeShipBase",
            "/assets/ships-v2/bases/large/base.obj",
            TemplateSelectionMode.UniqueMesh),
        new(
            "FirstEditionAssignedDial",
            "AssignedDial",
            "/assets/dial/dialmodel.obj",
            TemplateSelectionMode.AssignedDial),
        new(
            "FirstEditionUnassignedDial",
            "UnassignedDial",
            "/assets/dial/dialmodel.obj",
            TemplateSelectionMode.UnassignedDial)
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var savePath = Path.GetFullPath(args[1]);

            ValidateFile(savePath, "TTS save");

            var pegCataloguePath = ResolvePath(
                repositoryRoot,
                args,
                "--peg-catalogue",
                "_unifiedtoolkit_reports/phase12b/ship-peg-catalogue/ship-peg-assets.json");
            ValidateFile(pegCataloguePath, "Phase 12B-2A peg catalogue");

            var outputFolder = ResolveOutputFolder(repositoryRoot, args);
            var assetBaseUrl = ResolveAssetBaseUrl(args);

            var saveRoot = JsonNode.Parse(File.ReadAllText(savePath))?.AsObject()
                ?? throw new InvalidDataException("Could not parse the TTS save.");

            var objects = new List<SaveObjectRecord>();
            CollectObjects(saveRoot["ObjectStates"], null, objects);

            var extracted = SaveRules
                .Select(rule => ExtractSaveTemplate(rule, objects))
                .ToList();

            var pegCatalogue = Read<PegCatalogueInput>(pegCataloguePath);
            var pegTemplates = pegCatalogue.Pegs
                .OrderBy(peg => peg.TemplateKey, StringComparer.OrdinalIgnoreCase)
                .Select(peg => BuildPegTemplate(
                    repositoryRoot,
                    assetBaseUrl,
                    peg,
                    objects))
                .ToList();

            var requiredKeys = new[]
            {
                "FirstEditionSmallShipBase",
                "FirstEditionLargeShipBase",
                "FirstEditionAssignedDial",
                "FirstEditionSmallShipPeg",
                "FirstEditionBwingShipPeg",
                "FirstEditionLargeShipPeg"
            };

            var allTemplates = extracted
                .Concat(pegTemplates)
                .OrderBy(template => template.TemplateKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var available = allTemplates
                .Where(template => template.Status is "Extracted" or "RegisteredAsset")
                .Select(template => template.TemplateKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingRequired = requiredKeys
                .Where(key => !available.Contains(key))
                .ToList();

            var ambiguous = allTemplates
                .Where(template => template.Status == "Ambiguous")
                .Select(template => template.TemplateKey)
                .ToList();

            var errors = new List<string>();
            errors.AddRange(
                missingRequired.Select(key => $"Required runtime template '{key}' is missing."));
            errors.AddRange(
                ambiguous.Select(key => $"Runtime template '{key}' is ambiguous."));

            Directory.CreateDirectory(outputFolder);

            var manifest = new RuntimeTemplateExtractionManifest
            {
                SchemaVersion = "1.0.0",
                ImplementationVersion = "12B-2B-Fix3",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                SourceSave = NormalisePath(savePath),
                PegCataloguePath = NormalisePath(pegCataloguePath),
                AssetBaseUrl = assetBaseUrl,
                ObjectsInspected = objects.Count,
                TemplatesExtracted = allTemplates.Count(template =>
                    template.Status == "Extracted"),
                PegAssetsRegistered = allTemplates.Count(template =>
                    template.Status == "RegisteredAsset"),
                AmbiguousTemplates = ambiguous.Count,
                MissingRequiredTemplates = missingRequired.Count,
                ValidationErrors = errors,
                Templates = allTemplates
            };

            var manifestPath = Path.Combine(
                outputFolder,
                "runtime-templates.json");
            var csvPath = Path.Combine(
                outputFolder,
                "runtime-templates.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "RUNTIME-TEMPLATE-EXTRACTION.md");
            var snapshotsFolder = Path.Combine(
                outputFolder,
                "snapshots");

            Directory.CreateDirectory(snapshotsFolder);

            foreach (var template in allTemplates.Where(template =>
                         template.ObjectSnapshot is not null))
            {
                var snapshotPath = Path.Combine(
                    snapshotsFolder,
                    $"{SafeFileName(template.TemplateKey)}.json");

                File.WriteAllText(
                    snapshotPath,
                    template.ObjectSnapshot!.ToJsonString(JsonOptions),
                    new UTF8Encoding(false));

                template.SnapshotPath = NormalisePath(snapshotPath);
            }

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, allTemplates);
            WriteReport(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 12B-2B Exact Runtime Template Extraction");
            Console.WriteLine(
                "===============================================================");
            Console.WriteLine("Implementation:             12B-2B Fix 3");
            Console.WriteLine();
            Console.WriteLine($"Repository:                 {repositoryRoot}");
            Console.WriteLine($"TTS save:                   {savePath}");
            Console.WriteLine($"Peg catalogue:              {pegCataloguePath}");
            Console.WriteLine($"Asset base URL:             {assetBaseUrl}");
            Console.WriteLine();
            Console.WriteLine($"Objects inspected:          {manifest.ObjectsInspected}");
            Console.WriteLine($"Save templates extracted:   {manifest.TemplatesExtracted}");
            Console.WriteLine($"Peg assets registered:      {manifest.PegAssetsRegistered}");
            Console.WriteLine($"Ambiguous templates:        {manifest.AmbiguousTemplates}");
            Console.WriteLine($"Missing required templates: {manifest.MissingRequiredTemplates}");
            Console.WriteLine($"Validation errors:          {manifest.ValidationErrors.Count}");
            Console.WriteLine();

            foreach (var template in allTemplates)
            {
                Console.WriteLine(
                    $"  {template.TemplateKey,-32} " +
                    $"{template.Status,-15} " +
                    $"{template.Guid ?? template.RepositoryPath}");
            }

            Console.WriteLine();
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine($"CSV:                        {csvPath}");
            Console.WriteLine($"Snapshots:                  {snapshotsFolder}");
            Console.WriteLine($"Report:                     {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Runtime templates extracted. The source TTS save and repository assets were not modified.");

            return errors.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Runtime-template extraction failed: {ex.Message}");
            return 1;
        }
    }

    private static RuntimeTemplateRecord ExtractSaveTemplate(
        TemplateRule rule,
        IReadOnlyList<SaveObjectRecord> objects)
    {
        var candidates = objects
            .Where(item => MeshMatches(item.MeshUrl, rule.MeshSuffix))
            .Where(item => MatchesMode(item, rule.Mode))
            .OrderByDescending(item => RuntimeStrength(item, rule.Mode))
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return new RuntimeTemplateRecord
            {
                TemplateKey = rule.TemplateKey,
                TemplateType = rule.TemplateType,
                Status = rule.Mode == TemplateSelectionMode.UnassignedDial
                    ? "OptionalMissing"
                    : "Missing",
                MatchRule = rule.MeshSuffix
            };
        }

        // A working save can legitimately contain several spawned copies of
        // the same runtime class. They are not different template types.
        // Select one deterministically after ranking by runtime completeness.
        var selected = candidates[0];

        return new RuntimeTemplateRecord
        {
            TemplateKey = rule.TemplateKey,
            TemplateType = rule.TemplateType,
            Status = "Extracted",
            Guid = selected.Guid,
            Name = selected.Name,
            Nickname = selected.Nickname,
            Description = selected.Description,
            MeshUrl = selected.MeshUrl,
            DiffuseUrl = selected.DiffuseUrl,
            ColliderUrl = selected.ColliderUrl,
            LuaCharacters = selected.LuaCharacters,
            XmlCharacters = selected.XmlCharacters,
            SaveObjectPath = selected.Path,
            ParentGuid = selected.ParentGuid,
            MatchRule = rule.MeshSuffix,
            Notes = candidates.Count > 1
                ? $"Selected deterministically from {candidates.Count} runtime-identical candidates."
                : string.Empty,
            CandidateGuids = candidates
                .Select(item => item.Guid)
                .Where(value => value.Length > 0)
                .ToList(),
            CandidatePaths = candidates
                .Select(item => item.Path)
                .ToList(),
            ObjectSnapshot = selected.Object.DeepClone().AsObject()
        };
    }

    private static RuntimeTemplateRecord BuildPegTemplate(
        string repositoryRoot,
        string assetBaseUrl,
        PegCatalogueEntryInput peg,
        IReadOnlyList<SaveObjectRecord> objects)
    {
        var repositoryPath = peg.RepositoryPath
            .Replace('\\', '/')
            .TrimStart('/');

        var fileName = Path.GetFileName(repositoryPath);

        var saveCandidates = objects
            .Where(item =>
                item.MeshUrl.EndsWith(
                    "/" + fileName,
                    StringComparison.OrdinalIgnoreCase)
                && item.MeshUrl.Contains(
                    "/bases/pegs/",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => RuntimeStrength(
                item,
                TemplateSelectionMode.UniqueMesh))
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!peg.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeTemplateRecord
            {
                TemplateKey = peg.TemplateKey,
                TemplateType = peg.PegType + "Peg",
                Status = peg.Status,
                RepositoryPath = repositoryPath,
                MatchRule = fileName,
                CandidatePaths = peg.CandidatePaths
            };
        }

        if (saveCandidates.Count > 0)
        {
            var selected = saveCandidates[0];

            return new RuntimeTemplateRecord
            {
                TemplateKey = peg.TemplateKey,
                TemplateType = peg.PegType + "Peg",
                Status = "Extracted",
                Guid = selected.Guid,
                Name = selected.Name,
                Nickname = selected.Nickname,
                MeshUrl = selected.MeshUrl,
                DiffuseUrl = selected.DiffuseUrl,
                ColliderUrl = selected.ColliderUrl,
                LuaCharacters = selected.LuaCharacters,
                XmlCharacters = selected.XmlCharacters,
                SaveObjectPath = selected.Path,
                ParentGuid = selected.ParentGuid,
                RepositoryPath = repositoryPath,
                AssetUrl = CombineUrl(assetBaseUrl, repositoryPath),
                Sha256 = peg.Sha256,
                MatchRule = fileName,
                CandidateGuids = saveCandidates
                    .Select(item => item.Guid)
                    .Where(value => value.Length > 0)
                    .ToList(),
                CandidatePaths = saveCandidates
                    .Select(item => item.Path)
                    .ToList(),
                ObjectSnapshot = selected.Object.DeepClone().AsObject()
            };
        }

        return new RuntimeTemplateRecord
        {
            TemplateKey = peg.TemplateKey,
            TemplateType = peg.PegType + "Peg",
            Status = "RegisteredAsset",
            RepositoryPath = repositoryPath,
            AssetUrl = CombineUrl(assetBaseUrl, repositoryPath),
            Sha256 = peg.Sha256,
            MatchRule = fileName,
            Notes =
                "No standalone save object used this peg mesh. The prototype serializer " +
                "will create a Custom_Model peg using this authoritative OBJ asset."
        };
    }

    private static bool MatchesMode(
        SaveObjectRecord item,
        TemplateSelectionMode mode)
    {
        if (mode == TemplateSelectionMode.UniqueMesh)
            return true;

        // Do not inspect Lua/XML for the word "unassigned": the shared dial
        // runtime contains that term even on assigned pilot dials. The spawned
        // object's visible identity is the reliable discriminator.
        var visibleIdentity = string.Join(
            " ",
            item.Name,
            item.Nickname,
            item.Description);

        var unassigned = item.Nickname.Equals(
                "Unassigned Dial",
                StringComparison.OrdinalIgnoreCase)
            || visibleIdentity.Contains(
                "unassigned dial",
                StringComparison.OrdinalIgnoreCase);

        if (mode == TemplateSelectionMode.UnassignedDial)
            return unassigned;

        if (mode == TemplateSelectionMode.AssignedDial)
        {
            return !unassigned
                && !string.IsNullOrWhiteSpace(item.Nickname)
                && item.LuaCharacters > 0
                && item.XmlCharacters > 0;
        }

        return true;
    }

    private static int RuntimeStrength(
        SaveObjectRecord item,
        TemplateSelectionMode mode)
    {
        var score = 0;

        if (item.LuaCharacters > 0)
            score += Math.Min(100, item.LuaCharacters / 500);
        if (item.XmlCharacters > 0)
            score += Math.Min(60, item.XmlCharacters / 250);
        if (item.Object["ContainedObjects"] is JsonArray contained)
            score += Math.Min(20, contained.Count);
        if (item.Object["States"] is JsonObject states)
            score += Math.Min(20, states.Count);

        if (mode == TemplateSelectionMode.AssignedDial
            && item.Lua.Contains(
                "shipData",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (mode == TemplateSelectionMode.UnassignedDial
            && item.Nickname.Contains(
                "unassigned",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        return score;
    }

    private static bool MeshMatches(
        string meshUrl,
        string expectedSuffix)
    {
        if (meshUrl.Length == 0)
            return false;

        var normalised = meshUrl
            .Replace('\\', '/')
            .Split('?', '#')[0];

        // Some source URLs include branch prefixes, query strings or cache
        // suffixes. The authoritative repository-relative mesh identity can
        // therefore appear before the end of the URL.
        return normalised.Contains(
            expectedSuffix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectObjects(
        JsonNode? node,
        string? parentGuid,
        List<SaveObjectRecord> result,
        string path = "ObjectStates")
    {
        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                CollectObjects(
                    array[index],
                    parentGuid,
                    result,
                    $"{path}[{index}]");
            }

            return;
        }

        if (node is not JsonObject obj)
            return;

        var guid = Read(obj, "GUID");
        var mesh = obj["CustomMesh"] as JsonObject;

        result.Add(new SaveObjectRecord
        {
            Guid = guid,
            ParentGuid = parentGuid ?? string.Empty,
            Path = path,
            Name = Read(obj, "Name"),
            Nickname = Read(obj, "Nickname"),
            Description = Read(obj, "Description"),
            MeshUrl = Read(mesh, "MeshURL"),
            DiffuseUrl = Read(mesh, "DiffuseURL"),
            ColliderUrl = Read(mesh, "ColliderURL"),
            Lua = Read(obj, "LuaScript"),
            Xml = Read(obj, "XmlUI"),
            Object = obj
        });

        CollectObjects(
            obj["ContainedObjects"],
            guid.Length > 0 ? guid : parentGuid,
            result,
            path + ".ContainedObjects");

        CollectObjects(
            obj["States"],
            guid.Length > 0 ? guid : parentGuid,
            result,
            path + ".States");
    }

    private static string Read(
        JsonObject? obj,
        string property)
    {
        if (obj?[property] is JsonValue value
            && value.TryGetValue<string>(out var text))
        {
            return text ?? string.Empty;
        }

        return string.Empty;
    }

    private static T Read<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidDataException(
                   $"Could not parse JSON file: {path}");
    }

    private static string ResolvePath(
        string repositoryRoot,
        string[] args,
        string option,
        string relativeDefault)
    {
        var explicitPath = ReadOption(args, option);

        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(
                repositoryRoot,
                relativeDefault.Replace(
                    '/',
                    Path.DirectorySeparatorChar))
            : Path.GetFullPath(explicitPath);
    }

    private static string ResolveOutputFolder(
        string repositoryRoot,
        string[] args)
    {
        var explicitPath = ReadOption(args, "--output");

        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12b",
                "runtime-template-extraction")
            : Path.GetFullPath(explicitPath);
    }

    private static string ResolveAssetBaseUrl(string[] args)
    {
        var explicitUrl = ReadOption(args, "--asset-base-url");

        return string.IsNullOrWhiteSpace(explicitUrl)
            ? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/"
            : explicitUrl.TrimEnd('/') + "/";
    }

    private static string? ReadOption(
        string[] args,
        string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(
                    option,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string CombineUrl(
        string baseUrl,
        string relativePath) =>
        baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');

    private static string SafeFileName(string value) =>
        string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character)
                ? '_'
                : character));

    private static string NormalisePath(string path) =>
        path.Replace('\\', '/');

    private static void WriteCsv(
        string path,
        IEnumerable<RuntimeTemplateRecord> templates)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "TemplateKey,TemplateType,Status,GUID,Name,Nickname,MeshURL," +
            "DiffuseURL,ColliderURL,RepositoryPath,AssetURL,SHA256," +
            "LuaCharacters,XmlCharacters,SaveObjectPath,ParentGUID,MatchRule,Notes");

        foreach (var template in templates)
        {
            writer.WriteLine(string.Join(',',
                Csv(template.TemplateKey),
                Csv(template.TemplateType),
                Csv(template.Status),
                Csv(template.Guid ?? string.Empty),
                Csv(template.Name),
                Csv(template.Nickname),
                Csv(template.MeshUrl),
                Csv(template.DiffuseUrl),
                Csv(template.ColliderUrl),
                Csv(template.RepositoryPath),
                Csv(template.AssetUrl),
                Csv(template.Sha256),
                template.LuaCharacters,
                template.XmlCharacters,
                Csv(template.SaveObjectPath),
                Csv(template.ParentGuid),
                Csv(template.MatchRule),
                Csv(template.Notes)));
        }
    }

    private static void WriteReport(
        string path,
        RuntimeTemplateExtractionManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12B-2B – Exact Runtime Template Extraction");
        writer.WriteLine();
        writer.WriteLine($"Source save: `{manifest.SourceSave}`");
        writer.WriteLine();
        writer.WriteLine(
            "| Template | Type | Status | GUID / Asset |");
        writer.WriteLine("|---|---|---|---|");

        foreach (var template in manifest.Templates)
        {
            var identity = template.Guid
                ?? template.RepositoryPath;

            writer.WriteLine(
                $"| `{template.TemplateKey}` | {template.TemplateType} | " +
                $"{template.Status} | `{identity}` |");
        }

        if (manifest.ValidationErrors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Validation errors");
            foreach (var error in manifest.ValidationErrors)
                writer.WriteLine($"- {error}");
        }
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static void ValidateFile(
        string path,
        string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"{description} was not found.",
                path);
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  extract-runtime-templates <first-edition-repository> " +
            "<tts-save.json> [--peg-catalogue <file>] " +
            "[--asset-base-url <url>] [--output <folder>]");
    }

    private sealed record TemplateRule(
        string TemplateKey,
        string TemplateType,
        string MeshSuffix,
        TemplateSelectionMode Mode);

    private enum TemplateSelectionMode
    {
        UniqueMesh,
        AssignedDial,
        UnassignedDial
    }

    private sealed class SaveObjectRecord
    {
        public string Guid { get; init; } = string.Empty;
        public string ParentGuid { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Nickname { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string MeshUrl { get; init; } = string.Empty;
        public string DiffuseUrl { get; init; } = string.Empty;
        public string ColliderUrl { get; init; } = string.Empty;
        public string Lua { get; init; } = string.Empty;
        public string Xml { get; init; } = string.Empty;
        public int LuaCharacters => Lua.Length;
        public int XmlCharacters => Xml.Length;
        public JsonObject Object { get; init; } = new();
    }
}

public sealed class RuntimeTemplateExtractionManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string ImplementationVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string SourceSave { get; init; } = string.Empty;
    public string PegCataloguePath { get; init; } = string.Empty;
    public string AssetBaseUrl { get; init; } = string.Empty;
    public int ObjectsInspected { get; init; }
    public int TemplatesExtracted { get; init; }
    public int PegAssetsRegistered { get; init; }
    public int AmbiguousTemplates { get; init; }
    public int MissingRequiredTemplates { get; init; }
    public List<string> ValidationErrors { get; init; } = new();
    public List<RuntimeTemplateRecord> Templates { get; init; } = new();
}

public sealed class RuntimeTemplateRecord
{
    public string TemplateKey { get; init; } = string.Empty;
    public string TemplateType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Guid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string MeshUrl { get; init; } = string.Empty;
    public string DiffuseUrl { get; init; } = string.Empty;
    public string ColliderUrl { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string AssetUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public int LuaCharacters { get; init; }
    public int XmlCharacters { get; init; }
    public string SaveObjectPath { get; init; } = string.Empty;
    public string ParentGuid { get; init; } = string.Empty;
    public string MatchRule { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string SnapshotPath { get; set; } = string.Empty;
    public List<string> CandidateGuids { get; init; } = new();
    public List<string> CandidatePaths { get; init; } = new();
    public JsonObject? ObjectSnapshot { get; init; }
}

public sealed class PegCatalogueInput
{
    public List<PegCatalogueEntryInput> Pegs { get; init; } = new();
}

public sealed class PegCatalogueEntryInput
{
    public string TemplateKey { get; init; } = string.Empty;
    public string PegType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<string> CandidatePaths { get; init; } = new();
}
