using System.Text;
using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Commands;

public static class AuditOfficialFirstEditionContentCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> EpicShipIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "croccruiser",
        "gozanticlasscruiser",
        "gr75mediumtransport",
        "cr90corvette",
        "raiderclasscorvette"
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
            var explicitXWingData = ResolveExplicitXWingData(args);
            var xwingDataLayout = FirstEditionDataSourceResolver.Resolve(repositoryRoot, explicitXWingData);
            var xwingDataRoot = xwingDataLayout.DataRoot;
            var mappingFolder = ResolveMappingFolder(repositoryRoot, args, explicitXWingData is null ? 1 : 2);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);

            ValidateDirectory(repositoryRoot, "First Edition repository");
            ValidateDirectory(xwingDataRoot, "xwing-data repository");
            ValidateDirectory(mappingFolder, "First Edition mapping folder");

            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var officialPilots = FirstEditionDataLoader.LoadPilots(xwingDataRoot);
            var officialUpgrades = FirstEditionDataLoader.LoadUpgrades(xwingDataRoot);

            var pilotRows = AuditPilots(officialPilots, mappings);
            var upgradeRows = AuditUpgrades(officialUpgrades, mappings);

            Directory.CreateDirectory(outputFolder);

            var manifest = new OfficialContentAuditManifest
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Repository = repositoryRoot,
                XWingData = xwingDataRoot,
                MappingFolder = mappingFolder,
                MappingVersion = mappings.Version,
                EpicShipIds = EpicShipIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                PilotSummary = BuildSummary(pilotRows.Select(row => row.Status)),
                UpgradeSummary = BuildSummary(upgradeRows.Select(row => row.Status)),
                Pilots = pilotRows,
                Upgrades = upgradeRows
            };

            var manifestPath = Path.Combine(outputFolder, "official-first-edition-content-audit.json");
            var pilotCsvPath = Path.Combine(outputFolder, "official-pilot-content-audit.csv");
            var upgradeCsvPath = Path.Combine(outputFolder, "official-upgrade-content-audit.csv");
            var reportPath = Path.Combine(outputFolder, "OFFICIAL-FIRST-EDITION-CONTENT-AUDIT.md");

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WritePilotCsv(pilotCsvPath, pilotRows);
            WriteUpgradeCsv(upgradeCsvPath, upgradeRows);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 11D Official First Edition Content Audit");
            Console.WriteLine("==============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:      {repositoryRoot}");
            Console.WriteLine($"xwing-data:      {xwingDataRoot}");
            Console.WriteLine($"Mapping folder:  {mappingFolder}");
            Console.WriteLine($"Mapping version: {mappings.Version}");
            Console.WriteLine();
            PrintSummary("Pilots", manifest.PilotSummary);
            Console.WriteLine();
            PrintSummary("Upgrades", manifest.UpgradeSummary);
            Console.WriteLine();
            Console.WriteLine($"Manifest:      {manifestPath}");
            Console.WriteLine($"Pilot CSV:     {pilotCsvPath}");
            Console.WriteLine($"Upgrade CSV:   {upgradeCsvPath}");
            Console.WriteLine($"Report:        {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. No mappings or semantic entities were modified.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Official First Edition content audit failed: {ex.Message}");
            return 1;
        }
    }

    private static List<OfficialPilotAuditRow> AuditPilots(
        IReadOnlyList<FirstEditionDataPilot> official,
        ConversionMappingSet mappings)
    {
        var mappedKeys = mappings.Pilots
            .Select(mapping => PilotKey(mapping.TargetId, mapping.ShipId, mapping.Faction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var importedKeys = mappings.OfficialPilots
            .Select(pilot => PilotKey(pilot.Id, pilot.ShipId, pilot.Faction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var alternateKeys = mappings.PilotSourceAlternates
            .Select(alternate => PilotKey(alternate.TargetId, alternate.TargetShipId, alternate.TargetFaction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var alternateCounts = mappings.PilotSourceAlternates
            .GroupBy(
                alternate => PilotKey(alternate.TargetId, alternate.TargetShipId, alternate.TargetFaction),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return official
            .OrderBy(pilot => pilot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pilot => pilot.ShipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pilot => pilot.Faction, StringComparer.OrdinalIgnoreCase)
            .Select(pilot =>
            {
                var key = PilotKey(pilot.Id, pilot.ShipId, pilot.Faction);
                var status = importedKeys.Contains(key)
                    ? "OfficialOnlyImported"
                    : mappedKeys.Contains(key)
                        ? "Converted"
                        : alternateKeys.Contains(key)
                            ? "AlternatePrinting"
                            : EpicShipIds.Contains(pilot.ShipId)
                                ? "DeferredEpic"
                                : "Missing";

                return new OfficialPilotAuditRow
                {
                    Id = pilot.Id,
                    Name = pilot.Name,
                    ShipId = pilot.ShipId,
                    Faction = pilot.Faction,
                    PilotSkill = pilot.PilotSkill,
                    SquadPointCost = pilot.SquadPointCost,
                    Unique = pilot.Unique,
                    UpgradeSlots = pilot.UpgradeSlots.ToList(),
                    Status = status,
                    AlternateSourcePrintings = alternateCounts.TryGetValue(key, out var count) ? count : 0,
                    SourceFile = pilot.SourceFile,
                    Notes = status switch
                    {
                        "Converted" => "Canonical source-to-target pilot mapping exists.",
                        "OfficialOnlyImported" => "Native First Edition pilot is present in official-pilots.json.",
                        "AlternatePrinting" => "Only an alternate Unified source printing currently references this official identity.",
                        "DeferredEpic" => "Pilot belongs to one of the five explicitly deferred Epic ships.",
                        _ => "Official standard-ship pilot has not yet entered the semantic mapping/import pipeline."
                    }
                };
            })
            .ToList();
    }

    private static List<OfficialUpgradeAuditRow> AuditUpgrades(
        IReadOnlyList<FirstEditionDataUpgrade> official,
        ConversionMappingSet mappings)
    {
        var mappedKeys = mappings.Upgrades
            .Select(mapping => UpgradeKey(mapping.TargetId, mapping.Slot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var alternateKeys = mappings.UpgradeSourceAlternates
            .Select(alternate => UpgradeKey(alternate.TargetId, alternate.TargetSlot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var alternateCounts = mappings.UpgradeSourceAlternates
            .GroupBy(
                alternate => UpgradeKey(alternate.TargetId, alternate.TargetSlot),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return official
            .OrderBy(upgrade => upgrade.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(upgrade => upgrade.Slot, StringComparer.OrdinalIgnoreCase)
            .Select(upgrade =>
            {
                var key = UpgradeKey(upgrade.Id, upgrade.Slot);
                var status = mappedKeys.Contains(key)
                    ? "Converted"
                    : alternateKeys.Contains(key)
                        ? "AlternatePrinting"
                        : IsExplicitlyEpic(upgrade)
                            ? "DeferredEpic"
                            : "Missing";

                return new OfficialUpgradeAuditRow
                {
                    Id = upgrade.Id,
                    Name = upgrade.Name,
                    Slot = upgrade.Slot,
                    SquadPointCost = upgrade.SquadPointCost,
                    Unique = upgrade.Unique,
                    Factions = upgrade.Factions.ToList(),
                    ShipRestrictions = upgrade.ShipRestrictions.ToList(),
                    SizeRestrictions = upgrade.SizeRestrictions.ToList(),
                    Status = status,
                    AlternateSourcePrintings = alternateCounts.TryGetValue(key, out var count) ? count : 0,
                    SourceFile = upgrade.SourceFile,
                    Notes = status switch
                    {
                        "Converted" => "Canonical source-to-target upgrade mapping exists.",
                        "AlternatePrinting" => "Only an alternate Unified source printing currently references this official identity.",
                        "DeferredEpic" => "Upgrade has an explicit Epic ship or Epic/Huge size restriction.",
                        _ => "Official upgrade has not yet entered the semantic mapping/import pipeline."
                    }
                };
            })
            .ToList();
    }

    private static bool IsExplicitlyEpic(FirstEditionDataUpgrade upgrade)
    {
        if (upgrade.ShipRestrictions.Any(ship => EpicShipIds.Contains(Normalise(ship))))
            return true;

        return upgrade.SizeRestrictions.Any(size =>
        {
            var token = Normalise(size);
            return token is "huge" or "epic";
        });
    }

    private static OfficialContentAuditSummary BuildSummary(IEnumerable<string> statuses)
    {
        var values = statuses.ToList();
        return new OfficialContentAuditSummary
        {
            Total = values.Count,
            Converted = values.Count(status => status == "Converted"),
            OfficialOnlyImported = values.Count(status => status == "OfficialOnlyImported"),
            AlternatePrinting = values.Count(status => status == "AlternatePrinting"),
            DeferredEpic = values.Count(status => status == "DeferredEpic"),
            Missing = values.Count(status => status == "Missing")
        };
    }

    private static void PrintSummary(string label, OfficialContentAuditSummary summary)
    {
        Console.WriteLine($"{label}:");
        Console.WriteLine($"  Official records:          {summary.Total}");
        Console.WriteLine($"  Converted:                 {summary.Converted}");
        Console.WriteLine($"  Official-only imported:    {summary.OfficialOnlyImported}");
        Console.WriteLine($"  Alternate-printing only:   {summary.AlternatePrinting}");
        Console.WriteLine($"  Deferred Epic:             {summary.DeferredEpic}");
        Console.WriteLine($"  Missing:                   {summary.Missing}");
        Console.WriteLine($"  Accounted for:             {summary.AccountedFor}/{summary.Total}");
    }

    private static void WritePilotCsv(string path, IReadOnlyList<OfficialPilotAuditRow> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Id,Name,ShipId,Faction,PilotSkill,SquadPointCost,Unique,UpgradeSlots,Status,AlternateSourcePrintings,SourceFile,Notes");
        foreach (var row in rows)
            writer.WriteLine(string.Join(',', Csv(row.Id), Csv(row.Name), Csv(row.ShipId), Csv(row.Faction), row.PilotSkill, row.SquadPointCost, row.Unique, Csv(string.Join(';', row.UpgradeSlots)), Csv(row.Status), row.AlternateSourcePrintings, Csv(row.SourceFile), Csv(row.Notes)));
    }

    private static void WriteUpgradeCsv(string path, IReadOnlyList<OfficialUpgradeAuditRow> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Id,Name,Slot,SquadPointCost,Unique,Factions,ShipRestrictions,SizeRestrictions,Status,AlternateSourcePrintings,SourceFile,Notes");
        foreach (var row in rows)
            writer.WriteLine(string.Join(',', Csv(row.Id), Csv(row.Name), Csv(row.Slot), row.SquadPointCost, row.Unique, Csv(string.Join(';', row.Factions)), Csv(string.Join(';', row.ShipRestrictions)), Csv(string.Join(';', row.SizeRestrictions)), Csv(row.Status), row.AlternateSourcePrintings, Csv(row.SourceFile), Csv(row.Notes)));
    }

    private static void WriteMarkdown(string path, OfficialContentAuditManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Official First Edition Content Audit");
        writer.WriteLine();
        writer.WriteLine($"Generated: `{manifest.GeneratedUtc:O}`  ");
        writer.WriteLine($"Mapping version: `{manifest.MappingVersion}`");
        writer.WriteLine();
        writer.WriteLine("| Domain | Official | Converted | Official-only imported | Alternate-only | Deferred Epic | Missing |");
        writer.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
        writer.WriteLine($"| Pilots | {manifest.PilotSummary.Total} | {manifest.PilotSummary.Converted} | {manifest.PilotSummary.OfficialOnlyImported} | {manifest.PilotSummary.AlternatePrinting} | {manifest.PilotSummary.DeferredEpic} | {manifest.PilotSummary.Missing} |");
        writer.WriteLine($"| Upgrades | {manifest.UpgradeSummary.Total} | {manifest.UpgradeSummary.Converted} | {manifest.UpgradeSummary.OfficialOnlyImported} | {manifest.UpgradeSummary.AlternatePrinting} | {manifest.UpgradeSummary.DeferredEpic} | {manifest.UpgradeSummary.Missing} |");
        writer.WriteLine();
        writer.WriteLine("## Deferred Epic scope");
        writer.WriteLine();
        foreach (var ship in manifest.EpicShipIds)
            writer.WriteLine($"- `{ship}`");
        writer.WriteLine();
        writer.WriteLine("Upgrade records are classified as Deferred Epic only when xwing-data explicitly restricts them to an Epic ship or to Huge/Epic size. The audit does not guess based solely on upgrade-slot names.");
        writer.WriteLine();
        writer.WriteLine("## Missing standard pilots");
        writer.WriteLine();
        var missingPilots = manifest.Pilots.Where(row => row.Status == "Missing").ToList();
        if (missingPilots.Count == 0) writer.WriteLine("None.");
        else foreach (var row in missingPilots) writer.WriteLine($"- **{row.Name}** — `{row.Faction}/{row.ShipId}`");
        writer.WriteLine();
        writer.WriteLine("## Missing upgrades");
        writer.WriteLine();
        var missingUpgrades = manifest.Upgrades.Where(row => row.Status == "Missing").ToList();
        if (missingUpgrades.Count == 0) writer.WriteLine("None.");
        else foreach (var row in missingUpgrades) writer.WriteLine($"- **{row.Name}** — `{row.Slot}`");
    }

    private static string? ResolveExplicitXWingData(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            return null;

        var candidate = Path.GetFullPath(args[1]);
        return FirstEditionDataSourceResolver.LooksLikeDataSource(candidate)
            ? candidate
            : null;
    }

    private static string ResolveMappingFolder(string repositoryRoot, string[] args, int positionalStart)
    {
        var explicitOption = ReadOption(args, "--mapping-folder");
        if (!string.IsNullOrWhiteSpace(explicitOption))
            return Path.GetFullPath(explicitOption);

        var positional = args.Skip(positionalStart).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(positional))
            return Path.GetFullPath(positional);

        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition"),
            Path.Combine(Directory.GetCurrentDirectory(), "ConversionData", "first-edition"),
            Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition")
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static string ResolveOutputFolder(string repositoryRoot, string[] args)
    {
        var explicitOption = ReadOption(args, "--output");
        return string.IsNullOrWhiteSpace(explicitOption)
            ? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11d", "official-content-audit")
            : Path.GetFullPath(explicitOption);
    }

    private static string? ReadOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static string PilotKey(string id, string shipId, string faction) =>
        $"{Normalise(id)}\u001f{Normalise(shipId)}\u001f{Normalise(faction)}";

    private static string UpgradeKey(string id, string slot) =>
        $"{Normalise(id)}\u001f{Normalise(slot)}";

    private static string Normalise(string value) =>
        new((value ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static void ValidateDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{description} not found: {path}");
    }

    private static void ShowUsage() =>
        Console.WriteLine("Usage: UnifiedToolkit audit-official-first-edition-content <first-edition-repository> [xwing-data-folder] [mapping-folder] [--output <folder>]");
}

public sealed class OfficialContentAuditManifest
{
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Repository { get; init; } = string.Empty;
    public string XWingData { get; init; } = string.Empty;
    public string MappingFolder { get; init; } = string.Empty;
    public string MappingVersion { get; init; } = string.Empty;
    public List<string> EpicShipIds { get; init; } = new();
    public OfficialContentAuditSummary PilotSummary { get; init; } = new();
    public OfficialContentAuditSummary UpgradeSummary { get; init; } = new();
    public List<OfficialPilotAuditRow> Pilots { get; init; } = new();
    public List<OfficialUpgradeAuditRow> Upgrades { get; init; } = new();
}

public sealed class OfficialContentAuditSummary
{
    public int Total { get; init; }
    public int Converted { get; init; }
    public int OfficialOnlyImported { get; init; }
    public int AlternatePrinting { get; init; }
    public int DeferredEpic { get; init; }
    public int Missing { get; init; }
    public int AccountedFor => Converted + OfficialOnlyImported + AlternatePrinting + DeferredEpic + Missing;
}

public sealed class OfficialPilotAuditRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> UpgradeSlots { get; init; } = new();
    public string Status { get; init; } = string.Empty;
    public int AlternateSourcePrintings { get; init; }
    public string SourceFile { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class OfficialUpgradeAuditRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> Factions { get; init; } = new();
    public List<string> ShipRestrictions { get; init; } = new();
    public List<string> SizeRestrictions { get; init; } = new();
    public string Status { get; init; } = string.Empty;
    public int AlternateSourcePrintings { get; init; }
    public string SourceFile { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}
