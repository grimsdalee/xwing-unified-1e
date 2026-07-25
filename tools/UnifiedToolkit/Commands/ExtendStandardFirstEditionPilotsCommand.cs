using System.Text;
using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Repository;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class ExtendStandardFirstEditionPilotsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly TargetShip[] Targets =
    [
        new("tierereaper", "tiereaper", "TIE Reaper",
        [
            "Scarif Base Pilot",
            "Major Vermeil",
            "Captain Feroph",
            "Vizier"
        ]),
        new("mg100starfortress", "bsf17bomber", "B/SF-17 Bomber",
        [
            "Crimson Leader",
            "Cobalt Leader",
            "Crimson Specialist",
            "Crimson Squadron Pilot"
        ])
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

            ValidateDirectory(repositoryRoot, "First Edition repository");
            ValidateDirectory(xwingDataRoot, "xwing-data repository");
            ValidateDirectory(mappingFolder, "First Edition mapping folder");

            var unifiedSourceRoot = ResolveUnifiedSourceRepositoryRoot(repositoryRoot);
            var sourceRepository = RepositoryLoader.Load(unifiedSourceRoot);
            var officialPilots = FirstEditionDataLoader.LoadPilots(xwingDataRoot);

            var pilotsPath = Path.Combine(mappingFolder, "pilots.json");
            var dispositionsPath = Path.Combine(mappingFolder, "pilot-dispositions.json");
            var alternatesPath = Path.Combine(mappingFolder, "pilot-source-alternates.json");
            var mappingSetPath = Path.Combine(mappingFolder, "mapping-set.json");
            var officialPilotsPath = Path.Combine(mappingFolder, "official-pilots.json");

            var mappings = Read<List<PilotMapping>>(pilotsPath);
            var dispositions = Read<List<PilotDisposition>>(dispositionsPath);
            var alternates = Read<List<PilotSourceAlternate>>(alternatesPath);
            var importedOfficialPilots = File.Exists(officialPilotsPath)
                ? Read<List<OfficialFirstEditionPilot>>(officialPilotsPath)
                : new List<OfficialFirstEditionPilot>();
            var records = new List<ResultRecord>();
            var proposedMappings = new List<PilotMapping>();

            foreach (var target in Targets)
            {
                var sourcePilots = sourceRepository.Pilots
                    .Where(x => SourceShipId(x).Equals(target.SourceShipId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var targetOfficialPilots = officialPilots
                    .Where(x => x.ShipId.Equals(target.TargetShipId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var expectedName in target.ExpectedPilotNames)
                {
                    var officialMatches = targetOfficialPilots
                        .Where(x => Normalise(x.Name) == Normalise(expectedName))
                        .ToList();

                    if (officialMatches.Count != 1)
                    {
                        records.Add(ResultRecord.Error(target, expectedName,
                            officialMatches.Count == 0
                                ? "Expected official First Edition pilot was not found in xwing-data."
                                : $"{officialMatches.Count} official First Edition records matched the expected pilot name."));
                        continue;
                    }

                    var official = officialMatches[0];
                    var existing = mappings.FirstOrDefault(x =>
                        x.TargetId.Equals(official.Id, StringComparison.OrdinalIgnoreCase) &&
                        x.ShipId.Equals(target.TargetShipId, StringComparison.OrdinalIgnoreCase) &&
                        Normalise(x.Faction) == Normalise(official.Faction));

                    if (existing is not null)
                    {
                        records.Add(ResultRecord.AlreadyMapped(target, official, existing.SourceId));
                        continue;
                    }

                    var exactSourceMatches = sourcePilots
                        .Where(x => Normalise(x.Name) == Normalise(official.Name))
                        .ToList();

                    if (exactSourceMatches.Count == 0)
                    {
                        var importedOfficial = importedOfficialPilots.FirstOrDefault(x =>
                            x.Id.Equals(official.Id, StringComparison.OrdinalIgnoreCase) &&
                            x.ShipId.Equals(target.TargetShipId, StringComparison.OrdinalIgnoreCase) &&
                            Normalise(x.Faction) == Normalise(official.Faction));

                        if (importedOfficial is not null)
                        {
                            records.Add(ResultRecord.OfficialAlreadyImported(target, official,
                                "The pilot has already been imported through official-pilots.json and requires no Unified source mapping."));
                        }
                        else
                        {
                            records.Add(ResultRecord.OfficialOnly(target, official,
                                "No exact-name Unified 2.5 source pilot exists on the mapped source ship. " +
                                "This pilot requires an official-only semantic import path rather than a normal source mapping."));
                        }

                        continue;
                    }

                    if (exactSourceMatches.Count > 1)
                    {
                        records.Add(ResultRecord.Error(target, official.Name,
                            $"{exactSourceMatches.Count} Unified source pilots matched by name on {target.SourceShipId}."));
                        continue;
                    }

                    var source = exactSourceMatches[0];
                    var proposed = new PilotMapping
                    {
                        MappingId = $"pilot-{Slug(official.Id)}-{Slug(target.TargetShipId)}-{Slug(official.Faction)}-direct-v1",
                        SourceId = source.Id,
                        TargetId = official.Id,
                        Name = official.Name,
                        ShipId = target.TargetShipId,
                        Faction = official.Faction,
                        PilotSkill = official.PilotSkill,
                        SquadPointCost = official.SquadPointCost,
                        Unique = official.Unique,
                        UpgradeSlots = official.UpgradeSlots.ToArray()
                    };

                    proposedMappings.Add(proposed);
                    records.Add(ResultRecord.Proposed(target, official, source.Id, apply ? "Applied" : "Proposed"));
                }

                var unexpectedOfficial = targetOfficialPilots
                    .Where(x => !target.ExpectedPilotNames.Any(name => Normalise(name) == Normalise(x.Name)))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var unexpected in unexpectedOfficial)
                {
                    records.Add(new ResultRecord(
                        target.SourceShipId,
                        target.TargetShipId,
                        target.DisplayName,
                        unexpected.Id,
                        unexpected.Name,
                        "UnexpectedOfficialRecord",
                        "",
                        "xwing-data contains an additional pilot for this ship that is outside the explicitly approved First Edition list."));
                }
            }

            var validationIssues = PilotMappingValidator.Validate(mappings.Concat(proposedMappings), alternates).ToList();
            foreach (var issue in validationIssues)
                records.Add(new ResultRecord("", "", "", "", "", "ValidationError", "", issue));

            var errors = records.Count(x => x.Status is "Error" or "ValidationError");
            var officialOnly = records.Count(x => x.Status == "OfficialOnly");
            var officialAlreadyImported = records.Count(x => x.Status == "OfficialAlreadyImported");
            var outputFolder = Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11c", "standard-pilot-extension");
            Directory.CreateDirectory(outputFolder);
            var reportPath = Path.Combine(outputFolder, "STANDARD-PILOT-EXTENSION-REPORT.md");
            var manifestPath = Path.Combine(outputFolder, "standard-pilot-extension.json");
            var csvPath = Path.Combine(outputFolder, "standard-pilot-extension.csv");
            WriteReports(reportPath, manifestPath, csvPath, repositoryRoot, unifiedSourceRoot, xwingDataRoot,
                mappingFolder, apply, version, records);

            string backupFolder = "";
            if (apply)
            {
                if (errors > 0)
                    throw new InvalidOperationException("Pilot extension cannot be applied while validation errors remain. Review the generated report.");

                if (officialOnly > 0)
                    throw new InvalidOperationException(
                        "Pilot extension cannot be applied because one or more approved First Edition pilots have no Unified source pilot. " +
                        "The report identifies the records that require an official-only semantic import path.");

                backupFolder = CreateBackup(mappingFolder, pilotsPath, dispositionsPath, alternatesPath, mappingSetPath);
                mappings.AddRange(proposedMappings);
                mappings = mappings
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.ShipId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var promotedSourceIds = proposedMappings.Select(x => x.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                dispositions.RemoveAll(x => promotedSourceIds.Contains(x.SourceId));

                Write(pilotsPath, mappings);
                Write(dispositionsPath, dispositions.OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToList());
                Write(mappingSetPath, new MappingSetVersion { Version = version! });
            }

            Console.WriteLine("UnifiedToolkit Phase 11C-2 Standard Pilot Extension");
            Console.WriteLine("====================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:      {repositoryRoot}");
            Console.WriteLine($"Unified source:  {unifiedSourceRoot}");
            Console.WriteLine($"xwing-data:      {xwingDataRoot}");
            Console.WriteLine($"Mapping folder:  {mappingFolder}");
            Console.WriteLine($"Mode:            {(apply ? "Apply" : "Preview")}");
            if (!string.IsNullOrWhiteSpace(version)) Console.WriteLine($"Target version:  {version}");
            Console.WriteLine();

            foreach (var group in records.Where(x => x.Status != "UnexpectedOfficialRecord").GroupBy(x => x.TargetShipId))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) continue;
                Console.WriteLine(group.First().ShipName + ":");
                foreach (var record in group)
                    Console.WriteLine($"  {record.PilotName,-25} {record.Status,-14} {record.SourcePilotId}");
            }

            Console.WriteLine();
            Console.WriteLine($"Approved pilot identities: {Targets.Sum(x => x.ExpectedPilotNames.Count)}");
            Console.WriteLine($"Proposed/applied:           {records.Count(x => x.Status is "Proposed" or "Applied")}");
            Console.WriteLine($"Already mapped:             {records.Count(x => x.Status == "AlreadyMapped")}");
            Console.WriteLine($"Official-only required:     {officialOnly}");
            Console.WriteLine($"Official-only imported:     {officialAlreadyImported}");
            Console.WriteLine($"Unexpected source records:  {records.Count(x => x.Status == "UnexpectedOfficialRecord")}");
            Console.WriteLine($"Errors:                     {errors}");
            Console.WriteLine($"Report:                     {reportPath}");
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine($"CSV:                        {csvPath}");
            if (backupFolder.Length > 0) Console.WriteLine($"Backup:                     {backupFolder}");
            Console.WriteLine();
            Console.WriteLine(apply
                ? "Standard First Edition pilot mappings updated. Epic pilots remain deferred."
                : officialOnly > 0
                    ? "Preview only. Official-only records must be resolved before applying mappings."
                    : "Preview only. All official-only records are already imported; mapped pilots can be applied safely.");

            return errors == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Standard pilot extension failed: {ex.Message}");
            return 1;
        }
    }

    private static string SourceShipId(PilotDefinition pilot) => pilot.Ship?.Id ?? pilot.ShipType ?? "";

    private static string ResolveUnifiedSourceRepositoryRoot(string repositoryRoot)
    {
        var canonical = Path.Combine(repositoryRoot, "assets", "source", "unified25");
        var canonicalShipDb = Path.Combine(canonical, "TTS_xwing", "src", "Game", "Component", "Spawner", "ShipDb.lua");
        if (File.Exists(canonicalShipDb)) return canonical;

        var legacyShipDb = Path.Combine(repositoryRoot, "TTS_xwing", "src", "Game", "Component", "Spawner", "ShipDb.lua");
        if (File.Exists(legacyShipDb)) return repositoryRoot;

        throw new FileNotFoundException(
            "ShipDb.lua was not found in the canonical Unified 2.5 source folder or the legacy repository root. " +
            $"Checked: {canonicalShipDb} and {legacyShipDb}");
    }

    private static string ResolveMappingFolder(string[] args, string repositoryRoot)
    {
        var option = ReadOption(args, "--mapping-folder");
        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition")
            : Path.GetFullPath(option);
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
        var folder = Path.Combine(mappingFolder, "backups", $"phase11c-pilots-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        foreach (var file in files.Where(File.Exists))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
        return folder;
    }

    private static void WriteReports(string markdownPath, string jsonPath, string csvPath,
        string repositoryRoot, string unifiedSourceRoot, string xwingDataRoot, string mappingFolder,
        bool apply, string? version, IReadOnlyList<ResultRecord> records)
    {
        Write(jsonPath, new
        {
            generatedUtc = DateTime.UtcNow,
            repositoryRoot,
            unifiedSourceRoot,
            xwingDataRoot,
            mappingFolder,
            mode = apply ? "Apply" : "Preview",
            targetVersion = version,
            approvedPilotCount = Targets.Sum(x => x.ExpectedPilotNames.Count),
            deferredEpicShips = new[] { "croccruiser", "gozanticlasscruiser", "gr75mediumtransport", "cr90corvette", "raiderclasscorvette" },
            targets = Targets,
            records
        });

        var csvLines = new List<string> { "SourceShipId,TargetShipId,ShipName,PilotId,PilotName,Status,SourcePilotId,Notes" };
        csvLines.AddRange(records.Select(x => string.Join(',', new[]
        {
            Csv(x.SourceShipId), Csv(x.TargetShipId), Csv(x.ShipName), Csv(x.PilotId),
            Csv(x.PilotName), Csv(x.Status), Csv(x.SourcePilotId), Csv(x.Notes)
        })));
        File.WriteAllLines(csvPath, csvLines, new UTF8Encoding(false));

        var lines = new List<string>
        {
            "# Phase 11C-2 Standard Pilot Extension",
            "",
            $"- Mode: **{(apply ? "Apply" : "Preview")}**",
            $"- Target mapping version: **{version ?? "not supplied"}**",
            "- Approved TIE Reaper pilot identities: **4**",
            "- Approved B/SF-17 Bomber pilot identities: **4**",
            "- Epic pilots remain deferred.",
            "",
            "| Ship | Pilot | Source pilot | Status | Notes |",
            "|---|---|---|---|---|"
        };
        lines.AddRange(records.Select(x => $"| {x.ShipName} | {x.PilotName} | {x.SourcePilotId} | {x.Status} | {x.Notes.Replace("|", "\\|")} |"));
        lines.Add("");
        lines.Add("`OfficialOnly` means the First Edition pilot exists in xwing-data but has no exact source pilot in Unified 2.5 and has not yet been imported. `OfficialAlreadyImported` means that pilot is already authoritative in official-pilots.json, so it does not block applying the remaining mapped pilots.");
        File.WriteAllLines(markdownPath, lines, new UTF8Encoding(false));
    }

    private static string Csv(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }

    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Slug(string value) => Normalise(value);

    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}");
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  extend-standard-first-edition-pilots <repo-folder> <xwing-data-folder> [--mapping-folder <folder>] [--version <version>] [--apply]");
    }

    private sealed record TargetShip(string SourceShipId, string TargetShipId, string DisplayName, IReadOnlyList<string> ExpectedPilotNames);

    private sealed record ResultRecord(
        string SourceShipId,
        string TargetShipId,
        string ShipName,
        string PilotId,
        string PilotName,
        string Status,
        string SourcePilotId,
        string Notes)
    {
        public static ResultRecord Error(TargetShip ship, string pilotName, string notes) =>
            new(ship.SourceShipId, ship.TargetShipId, ship.DisplayName, "", pilotName, "Error", "", notes);

        public static ResultRecord OfficialOnly(TargetShip ship, FirstEditionDataPilot pilot, string notes) =>
            new(ship.SourceShipId, ship.TargetShipId, ship.DisplayName, pilot.Id, pilot.Name, "OfficialOnly", "", notes);

        public static ResultRecord OfficialAlreadyImported(TargetShip ship, FirstEditionDataPilot pilot, string notes) =>
            new(ship.SourceShipId, ship.TargetShipId, ship.DisplayName, pilot.Id, pilot.Name, "OfficialAlreadyImported", "", notes);

        public static ResultRecord AlreadyMapped(TargetShip ship, FirstEditionDataPilot pilot, string sourcePilotId) =>
            new(ship.SourceShipId, ship.TargetShipId, ship.DisplayName, pilot.Id, pilot.Name, "AlreadyMapped", sourcePilotId,
                "A live canonical pilot mapping already exists.");

        public static ResultRecord Proposed(TargetShip ship, FirstEditionDataPilot pilot, string sourcePilotId, string status) =>
            new(ship.SourceShipId, ship.TargetShipId, ship.DisplayName, pilot.Id, pilot.Name, status, sourcePilotId,
                "Exact pilot name and mapped source ship match.");
    }

    private sealed class MappingSetVersion
    {
        public string Version { get; init; } = "";
    }
}
