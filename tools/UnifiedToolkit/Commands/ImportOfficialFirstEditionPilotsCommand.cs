using System.Text;
using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Pilots;

namespace UnifiedToolkit.Commands;

public static class ImportOfficialFirstEditionPilotsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly string[] ApprovedPilotNames =
    [
        "Crimson Leader",
        "Cobalt Leader",
        "Crimson Specialist",
        "Crimson Squadron Pilot"
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

            var ships = ConversionMappingLoader.Load(mappingFolder).Ships;
            if (!ships.Any(x => x.TargetId.Equals("bsf17bomber", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The B/SF-17 Bomber ship mapping is not present. Apply Phase 11C-1 before importing its pilots.");

            var officialPilots = FirstEditionDataLoader.LoadPilots(xwingDataRoot)
                .Where(x => x.ShipId.Equals("bsf17bomber", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var officialPath = Path.Combine(mappingFolder, "official-pilots.json");
            var pilotsPath = Path.Combine(mappingFolder, "pilots.json");
            var mappingSetPath = Path.Combine(mappingFolder, "mapping-set.json");
            var existingOfficial = File.Exists(officialPath) ? Read<List<OfficialFirstEditionPilot>>(officialPath) : new List<OfficialFirstEditionPilot>();
            var mappedPilots = File.Exists(pilotsPath) ? Read<List<PilotMapping>>(pilotsPath) : new List<PilotMapping>();

            var records = new List<ResultRecord>();
            var proposed = new List<OfficialFirstEditionPilot>();

            foreach (var expectedName in ApprovedPilotNames)
            {
                var matches = officialPilots.Where(x => Normalise(x.Name) == Normalise(expectedName)).ToList();
                if (matches.Count != 1)
                {
                    records.Add(new ResultRecord("", expectedName, "Error", matches.Count == 0
                        ? "Approved pilot was not found in xwing-data."
                        : $"{matches.Count} xwing-data records matched the approved identity."));
                    continue;
                }

                var source = matches[0];
                var identity = Identity(source.Id, source.ShipId, source.Faction);
                if (existingOfficial.Any(x => Identity(x.Id, x.ShipId, x.Faction).Equals(identity, StringComparison.OrdinalIgnoreCase)))
                {
                    records.Add(new ResultRecord(source.Id, source.Name, "AlreadyImported", "The official pilot is already present in official-pilots.json."));
                    continue;
                }

                if (mappedPilots.Any(x => Identity(x.TargetId, x.ShipId, x.Faction).Equals(identity, StringComparison.OrdinalIgnoreCase)))
                {
                    records.Add(new ResultRecord(source.Id, source.Name, "Error", "The pilot identity already exists as a source mapping in pilots.json."));
                    continue;
                }

                proposed.Add(new OfficialFirstEditionPilot
                {
                    ImportId = $"official-pilot-{Slug(source.Id)}-{Slug(source.ShipId)}-{Slug(source.Faction)}-v1",
                    Id = source.Id,
                    Name = source.Name,
                    ShipId = source.ShipId,
                    Faction = source.Faction,
                    PilotSkill = source.PilotSkill,
                    SquadPointCost = source.SquadPointCost,
                    Unique = source.Unique,
                    UpgradeSlots = source.UpgradeSlots.ToArray(),
                    SourceDataset = "xwing-data",
                    SourceFile = MakeRelativePath(xwingDataRoot, source.SourceFile)
                });
                records.Add(new ResultRecord(source.Id, source.Name, apply ? "Imported" : "Proposed", "Authoritative First Edition pilot; no Unified 2.5 source pilot is required."));
            }

            foreach (var unexpected in officialPilots.Where(x => !ApprovedPilotNames.Any(n => Normalise(n) == Normalise(x.Name))))
                records.Add(new ResultRecord(unexpected.Id, unexpected.Name, "UnexpectedOfficialRecord", "Not included in the explicitly approved B/SF-17 Bomber pilot list."));

            var duplicateProposed = proposed.GroupBy(x => Identity(x.Id, x.ShipId, x.Faction), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).ToList();
            foreach (var duplicate in duplicateProposed)
                records.Add(new ResultRecord(duplicate.First().Id, duplicate.First().Name, "Error", $"Duplicate proposed identity: {duplicate.Key}"));

            var errors = records.Count(x => x.Status == "Error");
            var outputFolder = Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11c", "official-pilot-import");
            Directory.CreateDirectory(outputFolder);
            var reportPath = Path.Combine(outputFolder, "OFFICIAL-PILOT-IMPORT-REPORT.md");
            var manifestPath = Path.Combine(outputFolder, "official-pilot-import.json");
            var csvPath = Path.Combine(outputFolder, "official-pilot-import.csv");

            string backupFolder = "";
            if (apply)
            {
                if (errors > 0) throw new InvalidOperationException("Official pilot import cannot be applied while errors remain.");
                backupFolder = CreateBackup(mappingFolder, officialPath, mappingSetPath);
                existingOfficial.AddRange(proposed);
                existingOfficial = existingOfficial
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.ShipId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Faction, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Write(officialPath, existingOfficial);
                Write(mappingSetPath, new MappingSetVersion { Version = version! });
            }

            WriteReports(reportPath, manifestPath, csvPath, repositoryRoot, xwingDataRoot, mappingFolder, apply, version, records, proposed);

            Console.WriteLine("UnifiedToolkit Phase 11C-2B Official First Edition Pilot Import");
            Console.WriteLine("===============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:      {repositoryRoot}");
            Console.WriteLine($"xwing-data:      {xwingDataRoot}");
            Console.WriteLine($"Mapping folder:  {mappingFolder}");
            Console.WriteLine($"Mode:            {(apply ? "Apply" : "Preview")}");
            if (!string.IsNullOrWhiteSpace(version)) Console.WriteLine($"Target version:  {version}");
            Console.WriteLine();
            foreach (var record in records.Where(x => x.Status != "UnexpectedOfficialRecord"))
                Console.WriteLine($"{record.PilotName,-27} {record.Status,-16} {record.PilotId}");
            Console.WriteLine();
            Console.WriteLine($"Approved identities:       {ApprovedPilotNames.Length}");
            Console.WriteLine($"Proposed/imported:         {records.Count(x => x.Status is "Proposed" or "Imported")}");
            Console.WriteLine($"Already imported:          {records.Count(x => x.Status == "AlreadyImported")}");
            Console.WriteLine($"Unexpected source records: {records.Count(x => x.Status == "UnexpectedOfficialRecord")}");
            Console.WriteLine($"Errors:                    {errors}");
            Console.WriteLine($"Report:                    {reportPath}");
            Console.WriteLine($"Manifest:                  {manifestPath}");
            Console.WriteLine($"CSV:                       {csvPath}");
            if (backupFolder.Length > 0) Console.WriteLine($"Backup:                    {backupFolder}");
            Console.WriteLine();
            Console.WriteLine(apply
                ? "Official First Edition pilots imported. No Unified source mappings were fabricated."
                : "Preview only. Re-run with --version <version> --apply after reviewing the report.");
            return errors == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Official pilot import failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteReports(string markdownPath, string jsonPath, string csvPath, string repositoryRoot,
        string xwingDataRoot, string mappingFolder, bool apply, string? version,
        IReadOnlyList<ResultRecord> records, IReadOnlyList<OfficialFirstEditionPilot> proposed)
    {
        Write(jsonPath, new
        {
            generatedUtc = DateTime.UtcNow,
            repositoryRoot,
            xwingDataRoot,
            mappingFolder,
            mode = apply ? "Apply" : "Preview",
            targetVersion = version,
            targetShipId = "bsf17bomber",
            approvedPilotNames = ApprovedPilotNames,
            proposedOfficialPilots = proposed,
            records,
            deferredEpicShips = new[] { "croccruiser", "gozanticlasscruiser", "gr75mediumtransport", "cr90corvette", "raiderclasscorvette" }
        });

        var csv = new List<string> { "PilotId,PilotName,Status,Notes" };
        csv.AddRange(records.Select(x => string.Join(',', Csv(x.PilotId), Csv(x.PilotName), Csv(x.Status), Csv(x.Notes))));
        File.WriteAllLines(csvPath, csv, new UTF8Encoding(false));

        var lines = new List<string>
        {
            "# Phase 11C-2B Official First Edition Pilot Import",
            "",
            $"- Mode: **{(apply ? "Apply" : "Preview")}**",
            $"- Target mapping version: **{version ?? "not supplied"}**",
            "- Target ship: **B/SF-17 Bomber** (`bsf17bomber`)",
            "- Approved pilot identities: **4**",
            "- Unified 2.5 source mappings created: **0**",
            "- Epic pilots remain deferred.",
            "",
            "| Pilot | ID | Status | Notes |",
            "|---|---|---|---|"
        };
        lines.AddRange(records.Select(x => $"| {x.PilotName} | {x.PilotId} | {x.Status} | {x.Notes.Replace("|", "\\|")} |"));
        File.WriteAllLines(markdownPath, lines, new UTF8Encoding(false));
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
        var folder = Path.Combine(mappingFolder, "backups", $"phase11c-official-pilots-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        foreach (var file in files.Where(File.Exists))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
        return folder;
    }

    private static string Identity(string id, string shipId, string faction) => $"{id}|{shipId}|{faction}";
    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Slug(string value) => Normalise(value);
    private static string Csv(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }
    private static string MakeRelativePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
        catch { return path.Replace('\\', '/'); }
    }
    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}");
    }
    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  import-official-first-edition-pilots <first-edition-repository> <xwing-data-folder> [--mapping-folder <folder>] [--version <version>] [--apply]");
    }

    private sealed record ResultRecord(string PilotId, string PilotName, string Status, string Notes);
    private sealed class MappingSetVersion { public string Version { get; init; } = ""; }
}
