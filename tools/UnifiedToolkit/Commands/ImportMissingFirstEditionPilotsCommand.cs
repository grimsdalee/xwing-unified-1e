using System.Text;
using System.Text.Json;
using UnifiedToolkit.Conversion.Mapping.Pilots;

namespace UnifiedToolkit.Commands;

public static class ImportMissingFirstEditionPilotsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var proposalsPath = Path.GetFullPath(ReadOption(args, "--proposals")
                ?? Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports",
                    "pilot-completeness",
                    "missing-pilot-import-preparation",
                    "missing-first-edition-pilot-import-proposals.json"));

            var mappingFolder = Path.GetFullPath(ReadOption(args, "--mapping-folder")
                ?? Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition"));

            var apply = args.Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            var version = ReadOption(args, "--version");

            if (apply && string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("--version is required when --apply is used.");

            ValidateDirectory(repositoryRoot, "Repository");
            ValidateDirectory(mappingFolder, "First Edition mapping folder");

            if (!File.Exists(proposalsPath))
                throw new FileNotFoundException(
                    "Missing-pilot import proposals were not found. Run prepare-missing-first-edition-pilots first.",
                    proposalsPath);

            var preparation = Read<MissingPilotImportPreparationManifest>(proposalsPath);
            var readyProposals = preparation.Proposals
                .Where(item => item.Disposition.Equals("ReadyToImport", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PilotName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var deferredProposals = preparation.Proposals
                .Where(item => !item.Disposition.Equals("ReadyToImport", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PilotName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var officialPath = Path.Combine(mappingFolder, "official-pilots.json");
            var mappedPath = Path.Combine(mappingFolder, "pilots.json");
            var mappingSetPath = Path.Combine(mappingFolder, "mapping-set.json");

            var existingOfficial = File.Exists(officialPath)
                ? Read<List<OfficialFirstEditionPilot>>(officialPath)
                : new List<OfficialFirstEditionPilot>();

            var existingMapped = File.Exists(mappedPath)
                ? Read<List<PilotMapping>>(mappedPath)
                : new List<PilotMapping>();

            var records = new List<ImportRecord>();
            var proposedImports = new List<OfficialFirstEditionPilot>();
            var seenProposalIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var proposal in readyProposals)
            {
                var identity = Identity(proposal.PilotId, proposal.ShipId, proposal.Faction);

                if (!seenProposalIdentities.Add(identity))
                {
                    records.Add(ImportRecord.Error(
                        proposal,
                        "The preparation manifest contains the same pilot identity more than once."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(proposal.PilotId)
                    || string.IsNullOrWhiteSpace(proposal.PilotName)
                    || string.IsNullOrWhiteSpace(proposal.ShipId)
                    || string.IsNullOrWhiteSpace(proposal.Faction))
                {
                    records.Add(ImportRecord.Error(
                        proposal,
                        "One or more required pilot identity fields are empty."));
                    continue;
                }

                var officialMatch = existingOfficial.FirstOrDefault(item =>
                    Identity(item.Id, item.ShipId, item.Faction)
                        .Equals(identity, StringComparison.OrdinalIgnoreCase));

                if (officialMatch is not null)
                {
                    records.Add(ImportRecord.AlreadyImported(
                        proposal,
                        "The pilot identity already exists in official-pilots.json."));
                    continue;
                }

                var mappedMatch = existingMapped.FirstOrDefault(item =>
                    Identity(item.TargetId, item.ShipId, item.Faction)
                        .Equals(identity, StringComparison.OrdinalIgnoreCase));

                if (mappedMatch is not null)
                {
                    records.Add(ImportRecord.Error(
                        proposal,
                        $"The pilot identity already exists in pilots.json as mapping '{mappedMatch.MappingId}'."));
                    continue;
                }

                var import = new OfficialFirstEditionPilot
                {
                    ImportId = $"official-pilot-{Slug(proposal.PilotId)}-{Slug(proposal.ShipId)}-{Slug(proposal.Faction)}-v1",
                    Id = proposal.PilotId,
                    Name = proposal.PilotName,
                    ShipId = proposal.ShipId,
                    Faction = proposal.Faction,
                    PilotSkill = proposal.PilotSkill,
                    SquadPointCost = proposal.SquadPointCost,
                    Unique = proposal.Unique,
                    UpgradeSlots = proposal.UpgradeSlots.ToArray(),
                    SourceDataset = "xwing-data",
                    SourceFile = NormaliseSourceFile(repositoryRoot, proposal.SourceFile)
                };

                proposedImports.Add(import);
                records.Add(apply
                    ? ImportRecord.Imported(proposal)
                    : ImportRecord.Proposed(proposal));
            }

            foreach (var deferred in deferredProposals)
                records.Add(ImportRecord.Deferred(
                    deferred,
                    $"Not imported because its preparation disposition is '{deferred.Disposition}'."));

            var importIdDuplicates = proposedImports
                .GroupBy(item => item.ImportId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (var duplicate in importIdDuplicates)
            {
                foreach (var item in duplicate)
                {
                    var source = readyProposals.First(proposal =>
                        Identity(proposal.PilotId, proposal.ShipId, proposal.Faction)
                            .Equals(Identity(item.Id, item.ShipId, item.Faction), StringComparison.OrdinalIgnoreCase));

                    records.Add(ImportRecord.Error(
                        source,
                        $"Generated duplicate import ID '{item.ImportId}'."));
                }
            }

            var errors = records.Count(record => record.Status == "Error");
            var outputFolder = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "pilot-completeness",
                "missing-pilot-import");

            Directory.CreateDirectory(outputFolder);

            var manifestPath = Path.Combine(outputFolder, "missing-first-edition-pilot-import.json");
            var csvPath = Path.Combine(outputFolder, "missing-first-edition-pilot-import.csv");
            var reportPath = Path.Combine(outputFolder, "MISSING-FIRST-EDITION-PILOT-IMPORT.md");

            string backupFolder = string.Empty;

            if (apply)
            {
                if (errors > 0)
                    throw new InvalidOperationException(
                        "The missing-pilot import cannot be applied while validation errors remain.");

                backupFolder = CreateBackup(
                    mappingFolder,
                    officialPath,
                    mappedPath,
                    mappingSetPath);

                var combined = existingOfficial
                    .Concat(proposedImports)
                    .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ValidateFinalRegister(combined, existingMapped);

                Write(officialPath, combined);
                Write(mappingSetPath, new MappingSetVersion { Version = version! });
            }

            WriteOutputs(
                manifestPath,
                csvPath,
                reportPath,
                repositoryRoot,
                proposalsPath,
                mappingFolder,
                apply,
                version,
                existingOfficial.Count,
                proposedImports,
                deferredProposals,
                records,
                backupFolder);

            Console.WriteLine("UnifiedToolkit Missing First Edition Pilot Import");
            Console.WriteLine("=================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Proposals:               {proposalsPath}");
            Console.WriteLine($"Mapping folder:          {mappingFolder}");
            Console.WriteLine($"Mode:                    {(apply ? "Apply" : "Preview")}");
            if (!string.IsNullOrWhiteSpace(version))
                Console.WriteLine($"Target version:          {version}");
            Console.WriteLine();
            Console.WriteLine($"Ready proposals:         {readyProposals.Count}");
            Console.WriteLine($"Proposed/imported:       {records.Count(record => record.Status is "Proposed" or "Imported")}");
            Console.WriteLine($"Already imported:        {records.Count(record => record.Status == "AlreadyImported")}");
            Console.WriteLine($"Deferred epic/other:     {records.Count(record => record.Status == "Deferred")}");
            Console.WriteLine($"Errors:                  {errors}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            if (backupFolder.Length > 0)
                Console.WriteLine($"Backup:                  {backupFolder}");
            Console.WriteLine();
            Console.WriteLine(apply
                ? "Missing conventional First Edition pilots imported. Deferred epic entries were not modified."
                : "Preview only. Review the report, then rerun with --version <version> --apply.");

            return errors == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Missing First Edition pilot import failed: {ex.Message}");
            return 1;
        }
    }

    private static void ValidateFinalRegister(
        IReadOnlyList<OfficialFirstEditionPilot> officialPilots,
        IReadOnlyList<PilotMapping> mappedPilots)
    {
        var duplicateOfficial = officialPilots
            .GroupBy(
                item => Identity(item.Id, item.ShipId, item.Faction),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateOfficial.Count > 0)
            throw new InvalidOperationException(
                $"The final official pilot register would contain {duplicateOfficial.Count} duplicate identities.");

        var mappedIdentities = mappedPilots
            .Select(item => Identity(item.TargetId, item.ShipId, item.Faction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var collisions = officialPilots
            .Where(item => mappedIdentities.Contains(Identity(item.Id, item.ShipId, item.Faction)))
            .ToList();

        if (collisions.Count > 0)
            throw new InvalidOperationException(
                $"The final official pilot register would collide with {collisions.Count} mapped pilot identities.");
    }

    private static void WriteOutputs(
        string manifestPath,
        string csvPath,
        string reportPath,
        string repositoryRoot,
        string proposalsPath,
        string mappingFolder,
        bool apply,
        string? version,
        int existingOfficialCount,
        IReadOnlyList<OfficialFirstEditionPilot> proposedImports,
        IReadOnlyList<MissingPilotImportProposal> deferred,
        IReadOnlyList<ImportRecord> records,
        string backupFolder)
    {
        var manifest = new
        {
            schemaVersion = "1.0",
            generatedUtc = DateTimeOffset.UtcNow,
            repositoryRoot = NormalisePath(repositoryRoot),
            proposalsPath = NormalisePath(proposalsPath),
            mappingFolder = NormalisePath(mappingFolder),
            mode = apply ? "Apply" : "Preview",
            targetVersion = version,
            existingOfficialPilotCount = existingOfficialCount,
            proposedOrImportedCount = proposedImports.Count,
            finalOfficialPilotCount = apply
                ? existingOfficialCount + proposedImports.Count
                : existingOfficialCount,
            deferredCount = deferred.Count,
            errorCount = records.Count(record => record.Status == "Error"),
            backupFolder = NormalisePath(backupFolder),
            proposedOfficialPilots = proposedImports,
            deferredProposals = deferred,
            records
        };

        Write(manifestPath, manifest);

        var csv = new List<string>
        {
            "Faction,ShipId,PilotId,PilotName,Status,Disposition,ArtworkPath,Notes"
        };

        csv.AddRange(records.Select(record => string.Join(",",
            Csv(record.Faction),
            Csv(record.ShipId),
            Csv(record.PilotId),
            Csv(record.PilotName),
            Csv(record.Status),
            Csv(record.Disposition),
            Csv(record.ArtworkPath),
            Csv(record.Notes))));

        File.WriteAllLines(csvPath, csv, new UTF8Encoding(false));

        var report = new StringBuilder();
        report.AppendLine("# Missing First Edition Pilot Import");
        report.AppendLine();
        report.AppendLine($"- Mode: **{(apply ? "Apply" : "Preview")}**");
        report.AppendLine($"- Target mapping version: **{version ?? "not supplied"}**");
        report.AppendLine($"- Existing official-only pilots: **{existingOfficialCount}**");
        report.AppendLine($"- Proposed/imported conventional pilots: **{proposedImports.Count}**");
        report.AppendLine($"- Deferred epic/other entries: **{deferred.Count}**");
        report.AppendLine($"- Validation errors: **{records.Count(record => record.Status == "Error")}**");
        report.AppendLine();
        report.AppendLine("No Unified 2.5 source mappings are fabricated by this command.");
        report.AppendLine();

        foreach (var group in records
                     .GroupBy(record => new { record.Faction, record.ShipId })
                     .OrderBy(group => group.Key.Faction, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(group => group.Key.ShipId, StringComparer.OrdinalIgnoreCase))
        {
            report.AppendLine($"## {group.Key.Faction} / {group.Key.ShipId}");
            report.AppendLine();
            report.AppendLine("| Pilot | ID | Status | Notes |");
            report.AppendLine("|---|---|---|---|");

            foreach (var record in group.OrderBy(item => item.PilotName, StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine(
                    $"| {EscapeMarkdown(record.PilotName)} | `{record.PilotId}` | {record.Status} | {EscapeMarkdown(record.Notes)} |");
            }

            report.AppendLine();
        }

        File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
    }

    private static string CreateBackup(string mappingFolder, params string[] files)
    {
        var folder = Path.Combine(
            mappingFolder,
            "backups",
            $"phase13-missing-pilots-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        Directory.CreateDirectory(folder);

        foreach (var file in files.Where(File.Exists))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);

        return folder;
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not deserialize {path}.");

    private static void Write<T>(string path, T value) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{label} not found: {path}");
    }

    private static string Identity(string id, string shipId, string faction) =>
        $"{Normalise(id)}|{Normalise(shipId)}|{Normalise(faction)}";

    private static string Slug(string value) => Normalise(value);

    private static string Normalise(string value) =>
        new((value ?? string.Empty)
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormaliseSourceFile(string repositoryRoot, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
            return string.Empty;

        var normalised = sourceFile.Replace('\\', '/');
        var xwingDataRoot = Path.Combine(repositoryRoot, "source", "xwing-data");

        try
        {
            if (Path.IsPathRooted(sourceFile))
                return Path.GetRelativePath(xwingDataRoot, sourceFile).Replace('\\', '/');
        }
        catch
        {
            // Preserve the original path below if it cannot be relativised.
        }

        const string marker = "/source/xwing-data/";
        var markerIndex = normalised.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0
            ? normalised[(markerIndex + marker.Length)..]
            : normalised;
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{escaped}\""
            : escaped;
    }

    private static string EscapeMarkdown(string value) =>
        (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string NormalisePath(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\\', '/');

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  import-missing-first-edition-pilots <repository> [--proposals <file>] [--mapping-folder <folder>] [--version <version>] [--apply]");
    }

    private sealed class MappingSetVersion
    {
        public string Version { get; init; } = string.Empty;
    }

    private sealed record ImportRecord(
        string Faction,
        string ShipId,
        string PilotId,
        string PilotName,
        string Status,
        string Disposition,
        string ArtworkPath,
        string Notes)
    {
        public static ImportRecord Proposed(MissingPilotImportProposal proposal) =>
            From(proposal, "Proposed", "Authoritative First Edition pilot; no Unified 2.5 source mapping is required.");

        public static ImportRecord Imported(MissingPilotImportProposal proposal) =>
            From(proposal, "Imported", "Imported as an authoritative official-only First Edition pilot.");

        public static ImportRecord AlreadyImported(MissingPilotImportProposal proposal, string notes) =>
            From(proposal, "AlreadyImported", notes);

        public static ImportRecord Deferred(MissingPilotImportProposal proposal, string notes) =>
            From(proposal, "Deferred", notes);

        public static ImportRecord Error(MissingPilotImportProposal proposal, string notes) =>
            From(proposal, "Error", notes);

        private static ImportRecord From(
            MissingPilotImportProposal proposal,
            string status,
            string notes) =>
            new(
                proposal.Faction,
                proposal.ShipId,
                proposal.PilotId,
                proposal.PilotName,
                status,
                proposal.Disposition,
                proposal.ArtworkPath,
                notes);
    }
}
