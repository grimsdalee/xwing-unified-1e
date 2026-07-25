using System.Text;
using System.Text.Json;
using UnifiedToolkit.Conversion;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ExtendStandardFirstEditionShipsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly TargetDefinition[] Targets =
    [
        new("tierereaper", "tiereaper", "TIE Reaper", "small"),
        new("mg100starfortress", "bsf17bomber", "B/SF-17 Bomber", "large")
    ];

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
            var xwingDataRoot = Path.GetFullPath(args[1]);
            var mappingFolder = ResolveMappingFolder(args, repositoryRoot);
            var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            var version = ReadOption(args, "--version");

            if (apply && string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("--version is required when --apply is used.");

            ValidateDirectory(repositoryRoot, "Unified source repository");
            ValidateDirectory(xwingDataRoot, "xwing-data repository");
            ValidateDirectory(mappingFolder, "First Edition mapping folder");

            var unifiedSourceRepositoryRoot = ResolveUnifiedSourceRepositoryRoot(repositoryRoot);
            var sourceRepository = RepositoryLoader.Load(unifiedSourceRepositoryRoot);
            var officialShips = FirstEditionDataLoader.LoadShips(xwingDataRoot);
            var shipsPath = Path.Combine(mappingFolder, "ships.json");
            var dispositionsPath = Path.Combine(mappingFolder, "ship-dispositions.json");
            var mappingSetPath = Path.Combine(mappingFolder, "mapping-set.json");

            var mappings = Read<List<ShipMapping>>(shipsPath);
            var dispositions = Read<List<ShipDisposition>>(dispositionsPath);
            var results = new List<ResultRecord>();

            foreach (var target in Targets)
            {
                var sourceExists = sourceRepository.Ships.Any(x =>
                    x.Id.Equals(target.SourceId, StringComparison.OrdinalIgnoreCase));

                var official = officialShips.FirstOrDefault(x =>
                    Normalise(x.Id) == target.TargetId ||
                    Normalise(x.Name) == Normalise(target.Name));

                var existing = mappings.FirstOrDefault(x =>
                    x.SourceId.Equals(target.SourceId, StringComparison.OrdinalIgnoreCase));

                if (!sourceExists)
                {
                    results.Add(ResultRecord.Error(target, "Unified source ship was not found."));
                    continue;
                }

                if (official is null)
                {
                    results.Add(ResultRecord.Error(target, "Official First Edition xwing-data ship was not found."));
                    continue;
                }

                if (!official.Size.Equals(target.RequiredSize, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(ResultRecord.Error(target,
                        $"Official size '{official.Size}' does not match required First Edition size '{target.RequiredSize}'."));
                    continue;
                }

                var proposed = new ShipMapping
                {
                    MappingId = $"ship-{target.TargetId}-direct-v1",
                    SourceId = target.SourceId,
                    TargetId = target.TargetId,
                    Kind = ConversionKind.Direct,
                    Name = official.Name,
                    Size = target.RequiredSize,
                    Attack = official.Attack,
                    Agility = official.Agility,
                    Hull = official.Hull,
                    Shields = official.Shields,
                    Actions = official.Actions.ToList(),
                    Factions = official.Factions.Select(NormaliseFaction).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };

                if (existing is not null)
                {
                    results.Add(new ResultRecord(target.SourceId, target.TargetId, official.Name,
                        "AlreadyMapped", target.RequiredSize, official.Actions, official.Factions,
                        "A live ship mapping already exists; no replacement was made."));
                    continue;
                }

                mappings.Add(proposed);
                ReplaceDisposition(dispositions, target);
                results.Add(new ResultRecord(target.SourceId, target.TargetId, official.Name,
                    apply ? "Applied" : "Proposed", target.RequiredSize, official.Actions, official.Factions,
                    "Official First Edition standard-ship mapping prepared from xwing-data."));
            }

            var errors = results.Count(x => x.Status == "Error");
            var outputFolder = Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11c", "standard-ship-extension");
            Directory.CreateDirectory(outputFolder);
            var reportPath = Path.Combine(outputFolder, "STANDARD-SHIP-EXTENSION-REPORT.md");
            var jsonPath = Path.Combine(outputFolder, "standard-ship-extension.json");
            WriteReports(reportPath, jsonPath, repositoryRoot, xwingDataRoot, mappingFolder, apply, version, results);

            string backupFolder = "";
            if (apply && errors == 0)
            {
                backupFolder = CreateBackup(mappingFolder, shipsPath, dispositionsPath, mappingSetPath);
                mappings = mappings
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                dispositions = dispositions
                    .OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Write(shipsPath, mappings);
                Write(dispositionsPath, dispositions);
                Write(mappingSetPath, new MappingSetVersion { Version = version! });
            }

            Console.WriteLine("UnifiedToolkit Phase 11C-1 Standard Ship Extension");
            Console.WriteLine("===================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:      {repositoryRoot}");
            Console.WriteLine($"Unified source:  {unifiedSourceRepositoryRoot}");
            Console.WriteLine($"xwing-data:      {xwingDataRoot}");
            Console.WriteLine($"Mapping folder:  {mappingFolder}");
            Console.WriteLine($"Mode:            {(apply ? "Apply" : "Preview")}");
            if (!string.IsNullOrWhiteSpace(version)) Console.WriteLine($"Target version:  {version}");
            Console.WriteLine();
            foreach (var result in results)
                Console.WriteLine($"{result.SourceId,-22} -> {result.TargetId,-16} {result.Status}");
            Console.WriteLine();
            Console.WriteLine($"Proposed/applied: {results.Count(x => x.Status is "Proposed" or "Applied")}");
            Console.WriteLine($"Already mapped:   {results.Count(x => x.Status == "AlreadyMapped")}");
            Console.WriteLine($"Errors:            {errors}");
            Console.WriteLine($"Report:            {reportPath}");
            Console.WriteLine($"Manifest:          {jsonPath}");
            if (backupFolder.Length > 0) Console.WriteLine($"Backup:            {backupFolder}");
            Console.WriteLine();
            Console.WriteLine(apply
                ? "Standard First Edition ship mappings updated. Epic ships remain deferred."
                : "Preview only. Re-run with --version <version> --apply to update live mappings.");

            return errors == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Standard ship extension failed: {ex.Message}");
            return 1;
        }
    }

    private static void ReplaceDisposition(List<ShipDisposition> dispositions, TargetDefinition target)
    {
        dispositions.RemoveAll(x => x.SourceId.Equals(target.SourceId, StringComparison.OrdinalIgnoreCase));
        dispositions.Add(new ShipDisposition
        {
            SourceId = target.SourceId,
            Kind = ShipDispositionKind.Alias,
            ProposedTargetId = target.TargetId,
            Reason = "Official First Edition standard ship confirmed in xwing-data.",
            Notes = target.SourceId == "tierereaper"
                ? "Use the official First Edition Small base; do not retain the Unified 2.5 Medium base classification."
                : "Use the official First Edition Large base and B/SF-17 Bomber identity."
        });
    }


    private static string ResolveUnifiedSourceRepositoryRoot(string repositoryRoot)
    {
        var canonical = Path.Combine(repositoryRoot, "assets", "source", "unified25");
        var canonicalShipDb = Path.Combine(canonical, "TTS_xwing", "src", "Game", "Component", "Spawner", "ShipDb.lua");
        if (File.Exists(canonicalShipDb))
            return canonical;

        // Backward-compatible fallback for repositories that still place TTS_xwing at the root.
        var legacyShipDb = Path.Combine(repositoryRoot, "TTS_xwing", "src", "Game", "Component", "Spawner", "ShipDb.lua");
        if (File.Exists(legacyShipDb))
            return repositoryRoot;

        throw new FileNotFoundException(
            "ShipDb.lua was not found in the canonical Unified 2.5 source folder or the legacy repository root. " +
            $"Checked: {canonicalShipDb} and {legacyShipDb}");
    }

    private static string ResolveMappingFolder(string[] args, string repositoryRoot)
    {
        var option = ReadOption(args, "--mapping-folder");
        if (!string.IsNullOrWhiteSpace(option)) return Path.GetFullPath(option);
        return Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition");
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not deserialize {path}.");

    private static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));

    private static string CreateBackup(string mappingFolder, params string[] files)
    {
        var folder = Path.Combine(mappingFolder, "backups", $"phase11c-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        foreach (var file in files.Where(File.Exists)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
        return folder;
    }

    private static void WriteReports(string markdownPath, string jsonPath, string repositoryRoot,
        string xwingDataRoot, string mappingFolder, bool apply, string? version, List<ResultRecord> results)
    {
        var manifest = new
        {
            generatedUtc = DateTime.UtcNow,
            repositoryRoot,
            xwingDataRoot,
            mappingFolder,
            mode = apply ? "Apply" : "Preview",
            targetVersion = version,
            standardScope = new { uniqueShipTypesAfterApply = 49, factionShipCombinations = 56 },
            deferredEpicShips = new[] { "croccruiser", "gozanticlasscruiser", "gr75mediumtransport", "cr90corvette", "raiderclasscorvette" },
            records = results
        };
        Write(jsonPath, manifest);

        var lines = new List<string>
        {
            "# Phase 11C-1 Standard Ship Extension",
            "",
            $"- Mode: **{(apply ? "Apply" : "Preview")}**",
            $"- Target mapping version: **{version ?? "not supplied"}**",
            "- Standard target: **49 unique Small/Large ship types across 56 faction/ship combinations**",
            "- Epic scope deferred: **C-ROC, Gozanti, GR-75, CR90 and Raider**",
            "",
            "| Unified source | First Edition target | Name | Size | Status |",
            "|---|---|---|---|---|"
        };
        lines.AddRange(results.Select(x => $"| {x.SourceId} | {x.TargetId} | {x.Name} | {x.Size} | {x.Status} |"));
        lines.Add("");
        lines.Add("This command does not add or modify Epic ship mappings.");
        File.WriteAllLines(markdownPath, lines, new UTF8Encoding(false));
    }

    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string NormaliseFaction(string value) => Normalise(value) switch
    {
        "rebel" => "rebelalliance",
        "imperial" => "galacticempire",
        "scum" => "scumandvillainy",
        var faction => faction
    };

    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}");
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  extend-standard-first-edition-ships <repo-folder> <xwing-data-folder> [--mapping-folder <folder>] [--version <version>] [--apply]");
    }

    private sealed record TargetDefinition(string SourceId, string TargetId, string Name, string RequiredSize);
    private sealed record ResultRecord(string SourceId, string TargetId, string Name, string Status, string Size,
        IReadOnlyList<string> Actions, IReadOnlyList<string> Factions, string Notes)
    {
        public static ResultRecord Error(TargetDefinition target, string notes) =>
            new(target.SourceId, target.TargetId, target.Name, "Error", target.RequiredSize, [], [], notes);
    }

    private sealed class MappingSetVersion
    {
        public string Version { get; init; } = "";
    }
}
