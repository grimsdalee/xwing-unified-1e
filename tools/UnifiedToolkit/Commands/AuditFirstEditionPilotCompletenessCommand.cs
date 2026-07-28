using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Read-only audit comparing the authoritative xwing-data First Edition pilot
/// catalogue with the current mapping register, package plans, and imported
/// pilot artwork inventory.
/// </summary>
public static class AuditFirstEditionPilotCompletenessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
            ValidateDirectory(repositoryRoot, "First Edition repository");

            var xwingDataRoot = Path.GetFullPath(ReadOption(args, "--xwing-data")
                ?? Path.Combine(repositoryRoot, "source", "xwing-data"));
            var artworkRoot = Path.GetFullPath(ReadOption(args, "--artwork")
                ?? Path.Combine(repositoryRoot, "assets", "source", "xwing-data", "images"));
            var mappingFolder = Path.GetFullPath(ReadOption(args, "--mapping-folder")
                ?? Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition"));
            var packagePlanPath = Path.GetFullPath(ReadOption(args, "--package-plan")
                ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11", "ship-package-planning", "ship-package-plans.json"));
            var outputFolder = Path.GetFullPath(ReadOption(args, "--output")
                ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "pilot-completeness"));

            ValidateDirectory(xwingDataRoot, "xwing-data data and schema root");
            ValidateDirectory(artworkRoot, "xwing-data imported image root");
            ValidateDirectory(mappingFolder, "First Edition mapping folder");
            ValidateFile(packagePlanPath, "Phase 11 ship-package plan");

            var officialPilots = FirstEditionDataLoader.LoadPilots(xwingDataRoot);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var packages = LoadPackages(packagePlanPath);
            var artwork = BuildArtworkIndex(artworkRoot);

            var mappedKeys = mappings.Pilots
                .Select(item => Identity(item.TargetId, item.ShipId, item.Faction))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedKeys = mappings.OfficialPilots
                .Select(item => Identity(item.Id, item.ShipId, item.Faction))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var alternateKeys = mappings.PilotSourceAlternates
                .Select(item => Identity(item.TargetId, item.TargetShipId, item.TargetFaction))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = officialPilots
                .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PilotSkill)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(pilot => BuildRow(pilot, mappedKeys, importedKeys, alternateKeys, packages, artwork, artworkRoot))
                .ToList();

            var shipSummaries = rows
                .GroupBy(row => new { Faction = Normalise(row.Faction), ShipId = Normalise(row.ShipId) })
                .Select(group => new PilotCompletenessShipSummary
                {
                    Faction = group.First().Faction,
                    ShipId = group.First().ShipId,
                    OfficialPilots = group.Count(),
                    RegisteredPilots = group.Count(row => row.Registered),
                    AlternateOnlyPilots = group.Count(row => row.AlternateOnly),
                    PackageReadyPilots = group.Count(row => row.PackageReady),
                    MissingFromRegister = group.Count(row => !row.Registered),
                    MissingFromPackages = group.Count(row => row.Registered && !row.PackageReady),
                    ArtworkAvailable = group.Count(row => row.ArtworkAvailable),
                    MissingPilotNames = group.Where(row => !row.Registered).Select(row => row.Name).ToList()
                })
                .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var factionSummaries = rows
                .GroupBy(row => Normalise(row.Faction), StringComparer.OrdinalIgnoreCase)
                .Select(group => new PilotCompletenessFactionSummary
                {
                    Faction = group.First().Faction,
                    OfficialPilots = group.Count(),
                    RegisteredPilots = group.Count(row => row.Registered),
                    PackageReadyPilots = group.Count(row => row.PackageReady),
                    MissingFromRegister = group.Count(row => !row.Registered),
                    ArtworkAvailable = group.Count(row => row.ArtworkAvailable)
                })
                .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var manifest = new PilotCompletenessManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                XWingDataRoot = NormalisePath(xwingDataRoot),
                ArtworkRoot = NormalisePath(artworkRoot),
                MappingFolder = NormalisePath(mappingFolder),
                MappingVersion = mappings.Version,
                PackagePlanPath = NormalisePath(packagePlanPath),
                OfficialPilots = rows.Count,
                RegisteredPilots = rows.Count(row => row.Registered),
                AlternateOnlyPilots = rows.Count(row => row.AlternateOnly),
                PackageReadyPilots = rows.Count(row => row.PackageReady),
                MissingFromRegister = rows.Count(row => !row.Registered),
                MissingFromPackages = rows.Count(row => row.Registered && !row.PackageReady),
                ArtworkAvailable = rows.Count(row => row.ArtworkAvailable),
                ArtworkMissing = rows.Count(row => !row.ArtworkAvailable),
                Factions = factionSummaries,
                Ships = shipSummaries,
                Pilots = rows
            };

            Directory.CreateDirectory(outputFolder);
            var manifestPath = Path.Combine(outputFolder, "first-edition-pilot-completeness.json");
            var allCsvPath = Path.Combine(outputFolder, "first-edition-pilot-completeness.csv");
            var missingCsvPath = Path.Combine(outputFolder, "missing-first-edition-pilots.csv");
            var shipCsvPath = Path.Combine(outputFolder, "first-edition-pilot-counts-by-ship.csv");
            var reportPath = Path.Combine(outputFolder, "FIRST-EDITION-PILOT-COMPLETENESS.md");

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WritePilotCsv(allCsvPath, rows);
            WritePilotCsv(missingCsvPath, rows.Where(row => !row.Registered).ToList());
            WriteShipCsv(shipCsvPath, shipSummaries);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit First Edition Pilot Completeness Audit");
            Console.WriteLine("=====================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"xwing-data:             {xwingDataRoot}");
            Console.WriteLine($"Artwork:                {artworkRoot}");
            Console.WriteLine($"Mapping version:        {mappings.Version}");
            Console.WriteLine();
            Console.WriteLine($"Official pilots:        {manifest.OfficialPilots}");
            Console.WriteLine($"Registered pilots:      {manifest.RegisteredPilots}");
            Console.WriteLine($"Alternate-only pilots:  {manifest.AlternateOnlyPilots}");
            Console.WriteLine($"Package-ready pilots:   {manifest.PackageReadyPilots}");
            Console.WriteLine($"Missing from register:  {manifest.MissingFromRegister}");
            Console.WriteLine($"Missing from packages:  {manifest.MissingFromPackages}");
            Console.WriteLine($"Artwork available:      {manifest.ArtworkAvailable}");
            Console.WriteLine($"Artwork missing:        {manifest.ArtworkMissing}");
            Console.WriteLine();

            foreach (var ship in shipSummaries.Where(item => item.MissingFromRegister > 0 || item.MissingFromPackages > 0))
            {
                Console.WriteLine($"  {ship.Faction,-20} {ship.ShipId,-30} official {ship.OfficialPilots,3}  registered {ship.RegisteredPilots,3}  ready {ship.PackageReadyPilots,3}  missing {ship.MissingFromRegister,3}");
            }

            Console.WriteLine();
            Console.WriteLine($"Manifest:               {manifestPath}");
            Console.WriteLine($"All pilots CSV:         {allCsvPath}");
            Console.WriteLine($"Missing pilots CSV:     {missingCsvPath}");
            Console.WriteLine($"Ship counts CSV:        {shipCsvPath}");
            Console.WriteLine($"Report:                 {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. No mappings, semantic entities, packages, or assets were modified.");

            return manifest.MissingFromRegister == 0 && manifest.MissingFromPackages == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Pilot completeness audit failed: {ex.Message}");
            return 1;
        }
    }

    private static PilotCompletenessRow BuildRow(
        FirstEditionDataPilot pilot,
        HashSet<string> mappedKeys,
        HashSet<string> importedKeys,
        HashSet<string> alternateKeys,
        IReadOnlyList<PackageRecord> packages,
        ArtworkIndex artwork,
        string artworkRoot)
    {
        var key = Identity(pilot.Id, pilot.ShipId, pilot.Faction);
        var converted = mappedKeys.Contains(key);
        var officialOnly = importedKeys.Contains(key);
        var alternateOnly = !converted && !officialOnly && alternateKeys.Contains(key);
        var registered = converted || officialOnly;

        var package = packages.FirstOrDefault(item =>
            Normalise(item.ShipId) == Normalise(pilot.ShipId)
            && Normalise(item.Faction) == Normalise(pilot.Faction)
            && (Normalise(item.PilotId) == Normalise(pilot.Id)
                || Normalise(item.PilotName) == Normalise(pilot.Name)));

        var artworkPath = artwork.Find(pilot.Id, pilot.Name);

        return new PilotCompletenessRow
        {
            Id = pilot.Id,
            Name = pilot.Name,
            ShipId = pilot.ShipId,
            Faction = pilot.Faction,
            PilotSkill = pilot.PilotSkill,
            SquadPointCost = pilot.SquadPointCost,
            Unique = pilot.Unique,
            UpgradeSlots = pilot.UpgradeSlots.ToList(),
            ConvertedMapping = converted,
            OfficialOnlyImported = officialOnly,
            AlternateOnly = alternateOnly,
            Registered = registered,
            PackagePresent = package is not null,
            PackageStatus = package?.PackageStatus ?? string.Empty,
            PackageReady = package?.PackageStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase) == true,
            ArtworkAvailable = artworkPath is not null,
            ArtworkPath = artworkPath is null ? string.Empty : NormalisePath(Path.GetRelativePath(artworkRoot, artworkPath)),
            SourceFile = NormalisePath(pilot.SourceFile),
            Status = !registered
                ? alternateOnly ? "AlternateOnlyNotRegistered" : "MissingFromRegister"
                : package is null
                    ? "RegisteredNoPackage"
                    : package.PackageStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                        ? "Ready"
                        : "RegisteredPackageNotReady"
        };
    }

    private static List<PackageRecord> LoadPackages(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var array = FindArray(document.RootElement, "packages")
            ?? throw new InvalidDataException("The package-plan document does not contain a packages array.");

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new PackageRecord
            {
                PilotId = ReadString(item, "pilotId", "pilotXws", "pilotSlug", "pilotKey"),
                PilotName = ReadString(item, "pilotName", "name"),
                ShipId = ReadString(item, "shipId", "shipXws"),
                Faction = ReadString(item, "faction", "factionId"),
                PackageStatus = ReadString(item, "packageStatus", "status")
            })
            .Where(item => item.PilotName.Length > 0 && item.ShipId.Length > 0)
            .ToList();
    }

    private static JsonElement? FindArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array)
                    return property.Value;

                var nested = FindArray(property.Value, propertyName);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindArray(item, propertyName);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    private static ArtworkIndex BuildArtworkIndex(string root)
    {
        var preferredRoot = Directory.Exists(Path.Combine(root, "pilots"))
            ? Path.Combine(root, "pilots")
            : root;

        var files = Directory.EnumerateFiles(preferredRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ArtworkIndex(files);
    }

    private static void WritePilotCsv(string path, IReadOnlyList<PilotCompletenessRow> rows)
    {
        var output = new StringBuilder();
        output.AppendLine("Faction,ShipId,PilotId,PilotName,PilotSkill,Points,Unique,Status,Registered,ConvertedMapping,OfficialOnlyImported,AlternateOnly,PackagePresent,PackageStatus,PackageReady,ArtworkAvailable,ArtworkPath,SourceFile");
        foreach (var row in rows)
        {
            output.AppendLine(string.Join(",",
                Csv(row.Faction), Csv(row.ShipId), Csv(row.Id), Csv(row.Name), row.PilotSkill, row.SquadPointCost,
                row.Unique, Csv(row.Status), row.Registered, row.ConvertedMapping, row.OfficialOnlyImported,
                row.AlternateOnly, row.PackagePresent, Csv(row.PackageStatus), row.PackageReady,
                row.ArtworkAvailable, Csv(row.ArtworkPath), Csv(row.SourceFile)));
        }
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static void WriteShipCsv(string path, IReadOnlyList<PilotCompletenessShipSummary> rows)
    {
        var output = new StringBuilder();
        output.AppendLine("Faction,ShipId,OfficialPilots,RegisteredPilots,AlternateOnlyPilots,PackageReadyPilots,MissingFromRegister,MissingFromPackages,ArtworkAvailable,MissingPilotNames");
        foreach (var row in rows)
        {
            output.AppendLine(string.Join(",",
                Csv(row.Faction), Csv(row.ShipId), row.OfficialPilots, row.RegisteredPilots,
                row.AlternateOnlyPilots, row.PackageReadyPilots, row.MissingFromRegister,
                row.MissingFromPackages, row.ArtworkAvailable, Csv(string.Join(" | ", row.MissingPilotNames))));
        }
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static void WriteMarkdown(string path, PilotCompletenessManifest manifest)
    {
        var output = new StringBuilder();
        output.AppendLine("# First Edition Pilot Completeness Audit");
        output.AppendLine();
        output.AppendLine($"Generated: {manifest.GeneratedUtc:O}");
        output.AppendLine();
        output.AppendLine("## Summary");
        output.AppendLine();
        output.AppendLine($"- Official pilots: {manifest.OfficialPilots}");
        output.AppendLine($"- Registered pilots: {manifest.RegisteredPilots}");
        output.AppendLine($"- Alternate-only pilots: {manifest.AlternateOnlyPilots}");
        output.AppendLine($"- Package-ready pilots: {manifest.PackageReadyPilots}");
        output.AppendLine($"- Missing from register: {manifest.MissingFromRegister}");
        output.AppendLine($"- Missing from packages: {manifest.MissingFromPackages}");
        output.AppendLine($"- Artwork available: {manifest.ArtworkAvailable}");
        output.AppendLine($"- Artwork missing: {manifest.ArtworkMissing}");
        output.AppendLine();
        output.AppendLine("## Ship completeness");
        output.AppendLine();
        output.AppendLine("| Faction | Ship | Official | Registered | Ready | Missing | Missing pilots |");
        output.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var ship in manifest.Ships)
        {
            output.AppendLine($"| {ship.Faction} | {ship.ShipId} | {ship.OfficialPilots} | {ship.RegisteredPilots} | {ship.PackageReadyPilots} | {ship.MissingFromRegister} | {string.Join(", ", ship.MissingPilotNames)} |");
        }
        output.AppendLine();
        output.AppendLine("This audit is read-only. It does not modify mappings, semantic entities, package plans, or assets.");
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => string.Empty
            };
        }
        return string.Empty;
    }

    private static string Identity(string id, string shipId, string faction) =>
        $"{Normalise(id)}\u001f{Normalise(shipId)}\u001f{Normalise(faction)}";

    private static string Normalise(string value) =>
        new((value ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string SafeStem(string value) => Normalise(Path.GetFileNameWithoutExtension(value));

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string NormalisePath(string path) => path.Replace('\\', '/');

    private static void ValidateDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{description} not found: {path}");
    }

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} not found: {path}", path);
    }

    private static void ShowUsage() =>
        Console.WriteLine("Usage: UnifiedToolkit audit-first-edition-pilot-completeness <repository> [--xwing-data <source\\xwing-data>] [--artwork <assets\\source\\xwing-data\\images>] [--mapping-folder <folder>] [--package-plan <file>] [--output <folder>]");

    private sealed class ArtworkIndex
    {
        private readonly IReadOnlyList<string> _files;
        private readonly Dictionary<string, string> _byStem;

        public ArtworkIndex(IReadOnlyList<string> files)
        {
            _files = files;
            _byStem = files
                .GroupBy(SafeStem, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public string? Find(string id, string name)
        {
            var idKey = Normalise(id);
            foreach (var candidate in ExpandArtworkKeys(idKey))
            {
                if (_byStem.TryGetValue(candidate, out var exactId))
                    return exactId;
            }

            var nameKey = Normalise(name);
            foreach (var candidate in ExpandArtworkKeys(nameKey))
            {
                if (_byStem.TryGetValue(candidate, out var exactName))
                    return exactName;
            }

            return _files.FirstOrDefault(path =>
            {
                var stem = SafeStem(path);
                return ExpandArtworkKeys(idKey).Any(candidate =>
                           stem.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    || ExpandArtworkKeys(nameKey).Any(candidate =>
                           stem.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            });
        }

        private static IEnumerable<string> ExpandArtworkKeys(string key)
        {
            if (key.Length == 0)
                yield break;

            yield return key;

            // xwing-data abbreviates "Corvette" as "corv" in the Raider section artwork filenames.
            if (key.Equals("raiderclasscorvetteaft", StringComparison.OrdinalIgnoreCase))
                yield return "raiderclasscorvaft";
            else if (key.Equals("raiderclasscorvettefore", StringComparison.OrdinalIgnoreCase))
                yield return "raiderclasscorvfore";
        }
    }

    private sealed class PackageRecord
    {
        public string PilotId { get; init; } = string.Empty;
        public string PilotName { get; init; } = string.Empty;
        public string ShipId { get; init; } = string.Empty;
        public string Faction { get; init; } = string.Empty;
        public string PackageStatus { get; init; } = string.Empty;
    }
}

public sealed class PilotCompletenessManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string XWingDataRoot { get; init; } = string.Empty;
    public string ArtworkRoot { get; init; } = string.Empty;
    public string MappingFolder { get; init; } = string.Empty;
    public string MappingVersion { get; init; } = string.Empty;
    public string PackagePlanPath { get; init; } = string.Empty;
    public int OfficialPilots { get; init; }
    public int RegisteredPilots { get; init; }
    public int AlternateOnlyPilots { get; init; }
    public int PackageReadyPilots { get; init; }
    public int MissingFromRegister { get; init; }
    public int MissingFromPackages { get; init; }
    public int ArtworkAvailable { get; init; }
    public int ArtworkMissing { get; init; }
    public List<PilotCompletenessFactionSummary> Factions { get; init; } = new();
    public List<PilotCompletenessShipSummary> Ships { get; init; } = new();
    public List<PilotCompletenessRow> Pilots { get; init; } = new();
}

public sealed class PilotCompletenessFactionSummary
{
    public string Faction { get; init; } = string.Empty;
    public int OfficialPilots { get; init; }
    public int RegisteredPilots { get; init; }
    public int PackageReadyPilots { get; init; }
    public int MissingFromRegister { get; init; }
    public int ArtworkAvailable { get; init; }
}

public sealed class PilotCompletenessShipSummary
{
    public string Faction { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public int OfficialPilots { get; init; }
    public int RegisteredPilots { get; init; }
    public int AlternateOnlyPilots { get; init; }
    public int PackageReadyPilots { get; init; }
    public int MissingFromRegister { get; init; }
    public int MissingFromPackages { get; init; }
    public int ArtworkAvailable { get; init; }
    public List<string> MissingPilotNames { get; init; } = new();
}

public sealed class PilotCompletenessRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> UpgradeSlots { get; init; } = new();
    public bool ConvertedMapping { get; init; }
    public bool OfficialOnlyImported { get; init; }
    public bool AlternateOnly { get; init; }
    public bool Registered { get; init; }
    public bool PackagePresent { get; init; }
    public string PackageStatus { get; init; } = string.Empty;
    public bool PackageReady { get; init; }
    public bool ArtworkAvailable { get; init; }
    public string ArtworkPath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
