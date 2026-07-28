using System.Text;
using System.Text.Json;
using UnifiedToolkit.Conversion.Mapping.Pilots;

namespace UnifiedToolkit.Commands;

public static class ImportDeferredEpicFirstEditionPilotsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly HashSet<string> ApprovedEpicShipIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "croccruiser",
        "gozanticlasscruiser",
        "gr75mediumtransport",
        "cr90corvettefore",
        "cr90corvetteaft",
        "raiderclasscorvettefore",
        "raiderclasscorvetteaft"
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
                ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "pilot-completeness",
                    "missing-pilot-import-preparation", "missing-first-edition-pilot-import-proposals.json"));
            var mappingFolder = Path.GetFullPath(ReadOption(args, "--mapping-folder")
                ?? Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition"));
            var apply = args.Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            var version = ReadOption(args, "--version");

            if (apply && string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("--version is required when --apply is used.");
            if (!File.Exists(proposalsPath))
                throw new FileNotFoundException("Missing-pilot proposals were not found.", proposalsPath);

            var preparation = Read<MissingPilotImportPreparationManifest>(proposalsPath);
            var epic = preparation.Proposals
                .Where(item => item.Disposition.Equals("NeedsEpicShipSupport", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var officialPath = Path.Combine(mappingFolder, "official-pilots.json");
            var mappingSetPath = Path.Combine(mappingFolder, "mapping-set.json");
            var existing = File.Exists(officialPath)
                ? Read<List<OfficialFirstEditionPilot>>(officialPath)
                : new List<OfficialFirstEditionPilot>();

            var records = new List<EpicImportRecord>();
            var additions = new List<OfficialFirstEditionPilot>();

            foreach (var proposal in epic)
            {
                if (!ApprovedEpicShipIds.Contains(proposal.ShipId))
                {
                    records.Add(EpicImportRecord.Error(proposal, "The Epic ship identity is not in the explicit approved list."));
                    continue;
                }

                var identity = Identity(proposal.PilotId, proposal.ShipId, proposal.Faction);
                if (existing.Any(item => Identity(item.Id, item.ShipId, item.Faction)
                        .Equals(identity, StringComparison.OrdinalIgnoreCase)))
                {
                    records.Add(EpicImportRecord.AlreadyImported(proposal));
                    continue;
                }

                additions.Add(new OfficialFirstEditionPilot
                {
                    ImportId = $"official-epic-pilot-{Slug(proposal.PilotId)}-{Slug(proposal.ShipId)}-{Slug(proposal.Faction)}-v1",
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
                });
                records.Add(apply ? EpicImportRecord.Imported(proposal) : EpicImportRecord.Proposed(proposal));
            }

            var errors = records.Count(item => item.Status == "Error");
            var outputRoot = Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "pilot-completeness", "epic-pilot-import");
            Directory.CreateDirectory(outputRoot);
            var manifestPath = Path.Combine(outputRoot, "deferred-epic-first-edition-pilot-import.json");
            var csvPath = Path.Combine(outputRoot, "deferred-epic-first-edition-pilot-import.csv");
            var reportPath = Path.Combine(outputRoot, "DEFERRED-EPIC-FIRST-EDITION-PILOT-IMPORT.md");
            string backupFolder = string.Empty;

            if (apply)
            {
                if (errors > 0)
                    throw new InvalidOperationException("Epic pilot import cannot be applied while errors remain.");

                backupFolder = Path.Combine(mappingFolder, "backups", $"phase13c-epic-pilots-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
                Directory.CreateDirectory(backupFolder);
                foreach (var path in new[] { officialPath, mappingSetPath }.Where(File.Exists))
                    File.Copy(path, Path.Combine(backupFolder, Path.GetFileName(path)), true);

                var combined = existing.Concat(additions)
                    .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var duplicates = combined.GroupBy(item => Identity(item.Id, item.ShipId, item.Faction), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1).ToList();
                if (duplicates.Count > 0)
                    throw new InvalidOperationException($"The resulting official pilot register contains {duplicates.Count} duplicate identities.");

                Write(officialPath, combined);
                Write(mappingSetPath, new MappingSetVersion { Version = version! });
            }

            Write(manifestPath, new
            {
                schemaVersion = "1.0",
                generatedUtc = DateTimeOffset.UtcNow,
                mode = apply ? "Apply" : "Preview",
                targetVersion = version,
                existingOfficialPilotCount = existing.Count,
                proposedOrImportedCount = additions.Count,
                finalOfficialPilotCount = apply ? existing.Count + additions.Count : existing.Count,
                backupFolder = NormalisePath(backupFolder),
                records
            });

            var csv = new List<string> { "Faction,ShipId,PilotId,PilotName,Status,Notes" };
            csv.AddRange(records.Select(item => string.Join(',', Csv(item.Faction), Csv(item.ShipId), Csv(item.PilotId), Csv(item.PilotName), Csv(item.Status), Csv(item.Notes))));
            File.WriteAllLines(csvPath, csv, new UTF8Encoding(false));

            var report = new StringBuilder();
            report.AppendLine("# Deferred Epic First Edition Pilot Import");
            report.AppendLine();
            report.AppendLine($"- Mode: **{(apply ? "Apply" : "Preview")}**");
            report.AppendLine($"- Target mapping version: **{version ?? "not supplied"}**");
            report.AppendLine($"- Proposed/imported entries: **{additions.Count}**");
            report.AppendLine($"- Errors: **{errors}**");
            report.AppendLine();
            report.AppendLine("Fore/aft entries retain their official section ship IDs. Conversion resolves those IDs against their composite parent Epic ship solely for semantic registration.");
            report.AppendLine();
            report.AppendLine("| Faction | Ship ID | Entry | Status | Notes |");
            report.AppendLine("|---|---|---|---|---|");
            foreach (var item in records)
                report.AppendLine($"| {item.Faction} | `{item.ShipId}` | {Escape(item.PilotName)} | {item.Status} | {Escape(item.Notes)} |");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Deferred Epic First Edition Pilot Import");
            Console.WriteLine("========================================================");
            Console.WriteLine();
            Console.WriteLine($"Mode:                    {(apply ? "Apply" : "Preview")}");
            if (!string.IsNullOrWhiteSpace(version)) Console.WriteLine($"Target version:          {version}");
            Console.WriteLine($"Epic proposals:          {epic.Count}");
            Console.WriteLine($"Proposed/imported:       {additions.Count}");
            Console.WriteLine($"Already imported:        {records.Count(item => item.Status == "AlreadyImported")}");
            Console.WriteLine($"Errors:                  {errors}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            if (backupFolder.Length > 0) Console.WriteLine($"Backup:                  {backupFolder}");
            Console.WriteLine();
            Console.WriteLine(apply
                ? "Deferred Epic catalogue entries imported. Epic object assembly and runtime support remain separate phases."
                : "Preview only. Review the report, then rerun with --version <version> --apply.");
            return errors == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Deferred Epic pilot import failed: {exception.Message}");
            return 1;
        }
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not deserialize {path}.");
    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    private static string Identity(string id, string shipId, string faction) => $"{Normalise(id)}|{Normalise(shipId)}|{Normalise(faction)}";
    private static string Slug(string value) => Normalise(value);
    private static string Normalise(string value) => new((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string? ReadOption(string[] args, string name) { for (var i = 1; i < args.Length - 1; i++) if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1]; return null; }
    private static string Csv(string value) { value ??= string.Empty; var escaped = value.Replace("\"", "\"\""); return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped; }
    private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string NormalisePath(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    private static string NormaliseSourceFile(string repositoryRoot, string sourceFile)
    {
        var normalised = NormalisePath(sourceFile);
        const string marker = "/source/xwing-data/";
        var index = normalised.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? normalised[(index + marker.Length)..] : normalised;
    }
    private static void ShowUsage() => Console.WriteLine("  import-deferred-epic-first-edition-pilots <repository> [--proposals <file>] [--mapping-folder <folder>] [--version <version>] [--apply]");

    private sealed class MappingSetVersion { public string Version { get; init; } = string.Empty; }
    private sealed record EpicImportRecord(string Faction, string ShipId, string PilotId, string PilotName, string Status, string Notes)
    {
        public static EpicImportRecord Proposed(MissingPilotImportProposal p) => From(p, "Proposed", "Approved official Epic catalogue entry.");
        public static EpicImportRecord Imported(MissingPilotImportProposal p) => From(p, "Imported", "Imported as an authoritative official Epic catalogue entry.");
        public static EpicImportRecord AlreadyImported(MissingPilotImportProposal p) => From(p, "AlreadyImported", "The official Epic identity is already present.");
        public static EpicImportRecord Error(MissingPilotImportProposal p, string notes) => From(p, "Error", notes);
        private static EpicImportRecord From(MissingPilotImportProposal p, string status, string notes) => new(p.Faction, p.ShipId, p.PilotId, p.PilotName, status, notes);
    }
}
