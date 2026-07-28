using System.Globalization;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

public static class PrepareMissingFirstEditionPilotsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> EpicSectionShipIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cr90corvetteaft",
        "cr90corvettefore",
        "gozanticlasscruiser",
        "gr75mediumtransport",
        "raiderclasscorvetteaft",
        "raiderclasscorvettefore",
        "croccruiser"
    };

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 1)
            {
                ShowUsage();
                return 1;
            }

            var repositoryRoot = Path.GetFullPath(args[0]);
            var auditPath = Path.GetFullPath(GetOption(args, "--audit")
                ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "pilot-completeness",
                    "first-edition-pilot-completeness.json"));
            var outputRoot = Path.GetFullPath(GetOption(args, "--output")
                ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "pilot-completeness",
                    "missing-pilot-import-preparation"));

            if (!File.Exists(auditPath))
                throw new FileNotFoundException(
                    "Pilot-completeness audit manifest not found. Run audit-first-edition-pilot-completeness first.",
                    auditPath);

            Directory.CreateDirectory(outputRoot);

            var audit = JsonSerializer.Deserialize<PilotCompletenessManifest>(
                File.ReadAllText(auditPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Could not parse the pilot-completeness audit manifest.");

            var proposals = audit.Pilots
                .Where(pilot => !pilot.Registered)
                .OrderBy(pilot => pilot.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pilot => pilot.ShipId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pilot => pilot.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CreateProposal)
                .ToList();

            var manifest = new MissingPilotImportPreparationManifest
            {
                SchemaVersion = "1.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                AuditPath = NormalisePath(auditPath),
                OfficialPilotCount = audit.OfficialPilots,
                RegisteredPilotCount = audit.RegisteredPilots,
                MissingPilotCount = proposals.Count,
                ReadyToImport = proposals.Count(item => item.Disposition == "ReadyToImport"),
                NeedsEpicShipSupport = proposals.Count(item => item.Disposition == "NeedsEpicShipSupport"),
                NeedsArtwork = proposals.Count(item => item.Disposition == "NeedsArtwork"),
                Conflicts = proposals.Count(item => item.Disposition == "Conflict"),
                Proposals = proposals
            };

            var jsonPath = Path.Combine(outputRoot, "missing-first-edition-pilot-import-proposals.json");
            var csvPath = Path.Combine(outputRoot, "missing-first-edition-pilot-import-proposals.csv");
            var reportPath = Path.Combine(outputRoot, "MISSING-FIRST-EDITION-PILOT-IMPORT-PREPARATION.md");

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, proposals);
            WriteReport(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Missing First Edition Pilot Import Preparation");
            Console.WriteLine("==============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Audit:                   {auditPath}");
            Console.WriteLine();
            Console.WriteLine($"Official pilots:         {manifest.OfficialPilotCount}");
            Console.WriteLine($"Registered pilots:       {manifest.RegisteredPilotCount}");
            Console.WriteLine($"Missing pilots:          {manifest.MissingPilotCount}");
            Console.WriteLine($"Ready to import:         {manifest.ReadyToImport}");
            Console.WriteLine($"Needs epic support:      {manifest.NeedsEpicShipSupport}");
            Console.WriteLine($"Needs artwork:           {manifest.NeedsArtwork}");
            Console.WriteLine($"Conflicts:               {manifest.Conflicts}");
            Console.WriteLine();
            Console.WriteLine($"Proposals:               {jsonPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Preparation completed. No mappings, semantic entities, packages, or assets were modified.");

            return manifest.Conflicts > 0 || manifest.NeedsArtwork > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static MissingPilotImportProposal CreateProposal(PilotCompletenessRow pilot)
    {
        var disposition = EpicSectionShipIds.Contains(pilot.ShipId)
            ? "NeedsEpicShipSupport"
            : !pilot.ArtworkAvailable
                ? "NeedsArtwork"
                : string.IsNullOrWhiteSpace(pilot.Id)
                    || string.IsNullOrWhiteSpace(pilot.Name)
                    || string.IsNullOrWhiteSpace(pilot.ShipId)
                    || string.IsNullOrWhiteSpace(pilot.Faction)
                    ? "Conflict"
                    : "ReadyToImport";

        var reason = disposition switch
        {
            "NeedsEpicShipSupport" => "Official epic ship or ship-section entry; import requires explicit epic semantic handling.",
            "NeedsArtwork" => "No matching pilot artwork was resolved by the completeness audit.",
            "Conflict" => "One or more required official identity fields are empty.",
            _ => "Official pilot is absent from the semantic register and has resolvable artwork."
        };

        return new MissingPilotImportProposal
        {
            PilotId = pilot.Id,
            PilotName = pilot.Name,
            Faction = pilot.Faction,
            ShipId = pilot.ShipId,
            PilotSkill = pilot.PilotSkill,
            SquadPointCost = pilot.SquadPointCost,
            Unique = pilot.Unique,
            UpgradeSlots = pilot.UpgradeSlots.ToList(),
            ArtworkAvailable = pilot.ArtworkAvailable,
            ArtworkPath = pilot.ArtworkPath,
            SourceFile = pilot.SourceFile,
            Disposition = disposition,
            Reason = reason
        };
    }

    private static void WriteCsv(string path, IReadOnlyList<MissingPilotImportProposal> proposals)
    {
        var output = new StringBuilder();
        output.AppendLine("Faction,ShipId,PilotId,PilotName,PilotSkill,Points,Unique,Disposition,Reason,ArtworkAvailable,ArtworkPath,SourceFile,UpgradeSlots");

        foreach (var item in proposals)
        {
            output.AppendLine(string.Join(",",
                Csv(item.Faction),
                Csv(item.ShipId),
                Csv(item.PilotId),
                Csv(item.PilotName),
                item.PilotSkill.ToString(CultureInfo.InvariantCulture),
                item.SquadPointCost.ToString(CultureInfo.InvariantCulture),
                item.Unique,
                Csv(item.Disposition),
                Csv(item.Reason),
                item.ArtworkAvailable,
                Csv(item.ArtworkPath),
                Csv(item.SourceFile),
                Csv(string.Join(" | ", item.UpgradeSlots))));
        }

        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static void WriteReport(string path, MissingPilotImportPreparationManifest manifest)
    {
        var output = new StringBuilder();
        output.AppendLine("# Missing First Edition Pilot Import Preparation");
        output.AppendLine();
        output.AppendLine($"Generated: {manifest.GeneratedUtc:O}");
        output.AppendLine();
        output.AppendLine("| Metric | Count |");
        output.AppendLine("|---|---:|");
        output.AppendLine($"| Official pilots | {manifest.OfficialPilotCount} |");
        output.AppendLine($"| Registered pilots | {manifest.RegisteredPilotCount} |");
        output.AppendLine($"| Missing pilots | {manifest.MissingPilotCount} |");
        output.AppendLine($"| Ready to import | {manifest.ReadyToImport} |");
        output.AppendLine($"| Needs epic ship support | {manifest.NeedsEpicShipSupport} |");
        output.AppendLine($"| Needs artwork | {manifest.NeedsArtwork} |");
        output.AppendLine($"| Conflicts | {manifest.Conflicts} |");
        output.AppendLine();

        foreach (var group in manifest.Proposals
                     .GroupBy(item => new { item.Faction, item.ShipId })
                     .OrderBy(group => group.Key.Faction, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(group => group.Key.ShipId, StringComparer.OrdinalIgnoreCase))
        {
            output.AppendLine($"## {group.Key.Faction} / {group.Key.ShipId}");
            output.AppendLine();
            foreach (var item in group)
                output.AppendLine($"- **{item.PilotName}** (`{item.PilotId}`): {item.Disposition} — {item.Reason}");
            output.AppendLine();
        }

        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string NormalisePath(string path) => path.Replace('\\', '/');

    private static void ShowUsage() =>
        Console.WriteLine("Usage: UnifiedToolkit prepare-missing-first-edition-pilots <repository> [--audit <first-edition-pilot-completeness.json>] [--output <folder>]");
}

public sealed class MissingPilotImportPreparationManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string AuditPath { get; init; } = string.Empty;
    public int OfficialPilotCount { get; init; }
    public int RegisteredPilotCount { get; init; }
    public int MissingPilotCount { get; init; }
    public int ReadyToImport { get; init; }
    public int NeedsEpicShipSupport { get; init; }
    public int NeedsArtwork { get; init; }
    public int Conflicts { get; init; }
    public List<MissingPilotImportProposal> Proposals { get; init; } = new();
}

public sealed class MissingPilotImportProposal
{
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> UpgradeSlots { get; init; } = new();
    public bool ArtworkAvailable { get; init; }
    public string ArtworkPath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
