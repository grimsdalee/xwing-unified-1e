using System.Text;
using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class PrepareMissingPilotPackageAssetsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
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
            var packagePlanPath = Path.GetFullPath(ReadOption(args, "--package-plan")
                ?? Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports",
                    "phase11",
                    "ship-package-planning",
                    "ship-package-plans.json"));

            var outputRoot = Path.GetFullPath(ReadOption(args, "--output")
                ?? Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports",
                    "pilot-completeness",
                    "missing-pilot-package-assets"));

            if (!File.Exists(packagePlanPath))
                throw new FileNotFoundException(
                    "Ship package plan not found. Run plan-ship-packages first.",
                    packagePlanPath);

            Directory.CreateDirectory(outputRoot);

            var plan = JsonSerializer.Deserialize<FirstEditionShipPackagePlanDocument>(
                File.ReadAllText(packagePlanPath),
                JsonOptions)
                ?? throw new InvalidDataException("Could not parse the ship package plan.");

            var records = new List<MissingPilotPackageAssetRecord>();

            foreach (var package in plan.Packages
                         .Where(item => item.PackageStatus == ShipPackageStatuses.UnresolvedRequiredAssets)
                         .OrderBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.ShipId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.PilotName, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var requirement in package.Requirements
                             .Where(item => item.Required && item.ResolutionStatus == "Missing"))
                {
                    records.Add(Classify(repositoryRoot, package, requirement));
                }
            }

            var manifest = new MissingPilotPackageAssetManifest
            {
                SchemaVersion = "1.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                PackagePlanPath = NormalisePath(packagePlanPath),
                MissingRoleCount = records.Count,
                AutoResolvablePilotCards = records.Count(item => item.Disposition == "AutoResolvablePilotCard"),
                TokenGenerationRequired = records.Count(item => item.Disposition == "TokenGenerationRequired"),
                NeedsReview = records.Count(item => item.Disposition == "NeedsReview"),
                Records = records
            };

            var manifestPath = Path.Combine(outputRoot, "missing-pilot-package-assets.json");
            var csvPath = Path.Combine(outputRoot, "missing-pilot-package-assets.csv");
            var reportPath = Path.Combine(outputRoot, "MISSING-PILOT-PACKAGE-ASSETS.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));

            WriteCsv(csvPath, records);
            WriteReport(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Missing Pilot Package Asset Preparation");
            Console.WriteLine("=======================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                  {repositoryRoot}");
            Console.WriteLine($"Package plan:                {packagePlanPath}");
            Console.WriteLine();
            Console.WriteLine($"Missing required roles:      {manifest.MissingRoleCount}");
            Console.WriteLine($"Auto-resolvable pilot cards: {manifest.AutoResolvablePilotCards}");
            Console.WriteLine($"Token generation required:   {manifest.TokenGenerationRequired}");
            Console.WriteLine($"Needs review:                {manifest.NeedsReview}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                    {manifestPath}");
            Console.WriteLine($"CSV:                         {csvPath}");
            Console.WriteLine($"Report:                      {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Preparation completed. No assets, links, semantic entities, or packages were modified.");

            return manifest.NeedsReview == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static MissingPilotPackageAssetRecord Classify(
        string repositoryRoot,
        FirstEditionShipPackagePlan package,
        FirstEditionShipPackageRequirement requirement)
    {
        if (requirement.Role.Equals(ShipPackageRoles.PilotCard, StringComparison.OrdinalIgnoreCase))
        {
            var artworkRoot = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "xwing-data",
                "images",
                "pilots");

            var pilotIdKey = Normalise(package.PilotId);
            var pilotNameKey = Normalise(package.PilotName);
            var factionKey = Normalise(package.Faction);
            var shipIdKey = Normalise(package.ShipId);
            var shipNameKey = Normalise(package.ShipName);

            var matches = Directory.Exists(artworkRoot)
                ? Directory.EnumerateFiles(artworkRoot, "*.*", SearchOption.AllDirectories)
                    .Where(path => IsImage(path))
                    .Where(path =>
                    {
                        var normalisedPath = Normalise(path);
                        var stem = Normalise(Path.GetFileNameWithoutExtension(path));

                        var correctFaction =
                            normalisedPath.Contains(factionKey, StringComparison.OrdinalIgnoreCase);
                        var correctShip =
                            normalisedPath.Contains(shipIdKey, StringComparison.OrdinalIgnoreCase)
                            || normalisedPath.Contains(shipNameKey, StringComparison.OrdinalIgnoreCase);
                        var exactPilot =
                            stem.Equals(pilotIdKey, StringComparison.OrdinalIgnoreCase)
                            || stem.Equals(pilotNameKey, StringComparison.OrdinalIgnoreCase);

                        return correctFaction && correctShip && exactPilot;
                    })
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            return new MissingPilotPackageAssetRecord
            {
                PackageId = package.PackageId,
                Faction = package.Faction,
                ShipId = package.ShipId,
                ShipName = package.ShipName,
                PilotId = package.PilotId,
                PilotName = package.PilotName,
                Role = requirement.Role,
                Disposition = matches.Count == 1
                    ? "AutoResolvablePilotCard"
                    : "NeedsReview",
                CandidatePaths = matches
                    .Select(path => NormalisePath(Path.GetRelativePath(repositoryRoot, path)))
                    .ToList(),
                Note = matches.Count switch
                {
                    0 => "No exact xwing-data pilot-card image matched the faction, ship, and official pilot ID or name.",
                    1 => "The package planner can resolve this authoritative pilot card by exact faction, ship, and pilot identity.",
                    _ => "More than one exact pilot-card image matched the faction, ship, and official pilot identity."
                }
            };
        }

        if (requirement.Role.Equals(ShipPackageRoles.PilotBaseToken, StringComparison.OrdinalIgnoreCase))
        {
            return new MissingPilotPackageAssetRecord
            {
                PackageId = package.PackageId,
                Faction = package.Faction,
                ShipId = package.ShipId,
                ShipName = package.ShipName,
                PilotId = package.PilotId,
                PilotName = package.PilotName,
                Role = requirement.Role,
                Disposition = "TokenGenerationRequired",
                CandidatePaths = new List<string>(),
                Note =
                    "No canonical generated PilotBaseToken exists. Run the existing pilot-token generation workflow for this pilot."
            };
        }

        return new MissingPilotPackageAssetRecord
        {
            PackageId = package.PackageId,
            Faction = package.Faction,
            ShipId = package.ShipId,
            ShipName = package.ShipName,
            PilotId = package.PilotId,
            PilotName = package.PilotName,
            Role = requirement.Role,
            Disposition = "NeedsReview",
            CandidatePaths = new List<string>(),
            Note = "This required role is not covered by an automatic Phase 13B rule."
        };
    }

    private static void WriteCsv(
        string path,
        IReadOnlyList<MissingPilotPackageAssetRecord> records)
    {
        var lines = new List<string>
        {
            "Faction,ShipId,ShipName,PilotId,PilotName,Role,Disposition,CandidatePaths,Note"
        };

        lines.AddRange(records.Select(item => string.Join(",",
            Csv(item.Faction),
            Csv(item.ShipId),
            Csv(item.ShipName),
            Csv(item.PilotId),
            Csv(item.PilotName),
            Csv(item.Role),
            Csv(item.Disposition),
            Csv(string.Join(" | ", item.CandidatePaths)),
            Csv(item.Note))));

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(
        string path,
        MissingPilotPackageAssetManifest manifest)
    {
        var output = new StringBuilder();
        output.AppendLine("# Missing Pilot Package Asset Preparation");
        output.AppendLine();
        output.AppendLine("| Metric | Count |");
        output.AppendLine("|---|---:|");
        output.AppendLine($"| Missing required roles | {manifest.MissingRoleCount} |");
        output.AppendLine($"| Auto-resolvable pilot cards | {manifest.AutoResolvablePilotCards} |");
        output.AppendLine($"| Token generation required | {manifest.TokenGenerationRequired} |");
        output.AppendLine($"| Needs review | {manifest.NeedsReview} |");
        output.AppendLine();

        foreach (var group in manifest.Records
                     .GroupBy(item => item.Disposition)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            output.AppendLine($"## {group.Key}");
            output.AppendLine();

            foreach (var item in group
                         .OrderBy(value => value.Faction, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.ShipId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.PilotName, StringComparer.OrdinalIgnoreCase))
            {
                output.AppendLine(
                    $"- **{Escape(item.PilotName)}** — {item.Faction}/{item.ShipId}, `{item.Role}`: {Escape(item.Note)}");

                foreach (var candidate in item.CandidatePaths)
                    output.AppendLine($"  - `{candidate}`");
            }

            output.AppendLine();
        }

        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static bool IsImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static string Normalise(string value) =>
        new((value ?? string.Empty)
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalisePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');

    private static string Csv(string value)
    {
        value ??= string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{escaped}\""
            : escaped;
    }

    private static string Escape(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");

    private static void ShowUsage() =>
        Console.WriteLine(
            "  prepare-missing-pilot-package-assets <repository> [--package-plan <file>] [--output <folder>]");
}

public sealed class MissingPilotPackageAssetManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string PackagePlanPath { get; init; } = string.Empty;
    public int MissingRoleCount { get; init; }
    public int AutoResolvablePilotCards { get; init; }
    public int TokenGenerationRequired { get; init; }
    public int NeedsReview { get; init; }
    public List<MissingPilotPackageAssetRecord> Records { get; init; } = new();
}

public sealed class MissingPilotPackageAssetRecord
{
    public string PackageId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public List<string> CandidatePaths { get; init; } = new();
    public string Note { get; init; } = string.Empty;
}
