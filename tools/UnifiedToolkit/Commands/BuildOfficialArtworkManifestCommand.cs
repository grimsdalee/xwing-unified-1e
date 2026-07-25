using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;

namespace UnifiedToolkit.Commands;

public static class BuildOfficialArtworkManifestCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private static readonly Regex SideSuffix = new(
        @"(?<separator>[-_.\s])(?<side>front|back|fore|aft|reverse|rear|side[-_.\s]?[ab])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
            var outputRoot = ResolveOutput(repositoryRoot, args);

            var pilots = FirstEditionDataLoader.LoadPilots(xwingDataRoot);
            var upgrades = FirstEditionDataLoader.LoadUpgrades(xwingDataRoot);

            var pilotImagesRoot = Path.Combine(xwingDataLayout.ImagesRoot, "pilots");
            var upgradeImagesRoot = Path.Combine(xwingDataLayout.ImagesRoot, "upgrades");

            var pilotImages = ScanImages(repositoryRoot, pilotImagesRoot);
            var upgradeImages = ScanImages(repositoryRoot, upgradeImagesRoot);

            var pilotEntries = BuildPilotEntries(pilots, pilotImages);
            var upgradeEntries = BuildUpgradeEntries(upgrades, upgradeImages);

            Directory.CreateDirectory(outputRoot);

            var manifest = new OfficialArtworkManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Repository = repositoryRoot,
                XWingDataRoot = xwingDataRoot,
                XWingDataImagesRoot = xwingDataLayout.ImagesRoot,
                PilotDefinitionCount = pilots.Count,
                UpgradeDefinitionCount = upgrades.Count,
                PilotImageCount = pilotImages.Count,
                UpgradeImageCount = upgradeImages.Count,
                PilotEntries = pilotEntries,
                UpgradeEntries = upgradeEntries,
                UnassignedPilotImages = FindUnassigned(pilotImages, pilotEntries.SelectMany(entry => entry.Images)),
                UnassignedUpgradeImages = FindUnassigned(upgradeImages, upgradeEntries.SelectMany(entry => entry.Images))
            };

            var manifestPath = Path.Combine(outputRoot, "official-artwork-manifest.json");
            var pilotCsvPath = Path.Combine(outputRoot, "official-pilot-artwork.csv");
            var upgradeCsvPath = Path.Combine(outputRoot, "official-upgrade-artwork.csv");
            var reportPath = Path.Combine(outputRoot, "OFFICIAL-ARTWORK-MANIFEST-REPORT.md");

            WriteJson(manifestPath, manifest);
            WritePilotCsv(pilotCsvPath, pilotEntries);
            WriteUpgradeCsv(upgradeCsvPath, upgradeEntries);
            WriteMarkdown(reportPath, manifest);

            var doubleSided = upgradeEntries.Count(entry => entry.CardStructure == "DoubleSided");
            var ambiguous = upgradeEntries.Count(entry => entry.Status == "Ambiguous");
            var missing = upgradeEntries.Count(entry => entry.Status == "Missing");

            Console.WriteLine("UnifiedToolkit Phase 11E Official Artwork Manifest");
            Console.WriteLine("===================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"xwing-data data:        {xwingDataRoot}");
            Console.WriteLine($"xwing-data images:      {xwingDataLayout.ImagesRoot}");
            Console.WriteLine();
            Console.WriteLine($"Pilot definitions:      {pilots.Count}");
            Console.WriteLine($"Pilot images:           {pilotImages.Count}");
            Console.WriteLine($"Pilot entries matched:  {pilotEntries.Count(entry => entry.Status == "Matched")}");
            Console.WriteLine($"Epic fore/aft entries:  {pilotEntries.Count(entry => entry.CardStructure == "ForeAft")}");
            Console.WriteLine();
            Console.WriteLine($"Upgrade definitions:    {upgrades.Count}");
            Console.WriteLine($"Upgrade images:         {upgradeImages.Count}");
            Console.WriteLine($"Single-sided cards:     {upgradeEntries.Count(entry => entry.CardStructure == "SingleSided")}");
            Console.WriteLine($"Double-sided cards:     {doubleSided}");
            Console.WriteLine($"Ambiguous artwork:      {ambiguous}");
            Console.WriteLine($"Missing artwork:        {missing}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:               {manifestPath}");
            Console.WriteLine($"Pilot CSV:              {pilotCsvPath}");
            Console.WriteLine($"Upgrade CSV:            {upgradeCsvPath}");
            Console.WriteLine($"Report:                 {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Artwork manifest built. Source files were not modified.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Official artwork manifest failed: {ex.Message}");
            return 1;
        }
    }

    private static List<OfficialPilotArtworkEntry> BuildPilotEntries(
        IReadOnlyList<FirstEditionDataPilot> pilots,
        IReadOnlyList<OfficialArtworkImage> images)
    {
        var definitions = pilots
            .GroupBy(pilot => $"{Normalise(pilot.Id)}|{Normalise(pilot.ShipId)}|{Normalise(pilot.Faction)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var result = new List<OfficialPilotArtworkEntry>();

        foreach (var pilot in definitions)
        {
            var matches = images
                .Where(image => MatchesPilot(image, pilot))
                .OrderBy(image => SideOrder(image.Side))
                .ThenBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Add(new OfficialPilotArtworkEntry
            {
                Id = pilot.Id,
                Name = pilot.Name,
                ShipId = pilot.ShipId,
                Faction = pilot.Faction,
                CardStructure = ClassifyPilotStructure(matches),
                Status = matches.Count == 0 ? "Missing" : matches.Count <= 2 ? "Matched" : "Ambiguous",
                Images = matches
            });
        }

        // Huge ship section cards do not always have ordinary pilot definitions.
        foreach (var group in images
                     .Where(image => image.Side is "Fore" or "Aft")
                     .GroupBy(image => $"{Normalise(image.FactionFolder)}|{Normalise(image.EntityFolder)}", StringComparer.OrdinalIgnoreCase))
        {
            var groupImages = group.OrderBy(image => SideOrder(image.Side)).ToList();
            if (result.Any(entry => entry.Images.Any(image => groupImages.Any(candidate => candidate.RepositoryPath.Equals(image.RepositoryPath, StringComparison.OrdinalIgnoreCase)))))
                continue;

            result.Add(new OfficialPilotArtworkEntry
            {
                Id = Normalise(groupImages[0].EntityFolder),
                Name = groupImages[0].EntityFolder,
                ShipId = Normalise(groupImages[0].EntityFolder),
                Faction = Normalise(groupImages[0].FactionFolder),
                CardStructure = "ForeAft",
                Status = groupImages.Any(image => image.Side == "Fore") && groupImages.Any(image => image.Side == "Aft")
                    ? "Matched"
                    : "Ambiguous",
                Images = groupImages
            });
        }

        return result
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.ShipId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<OfficialUpgradeArtworkEntry> BuildUpgradeEntries(
        IReadOnlyList<FirstEditionDataUpgrade> upgrades,
        IReadOnlyList<OfficialArtworkImage> images)
    {
        var result = new List<OfficialUpgradeArtworkEntry>();

        foreach (var upgrade in upgrades)
        {
            var id = Normalise(upgrade.Id);
            var name = Normalise(upgrade.Name);

            var matches = images
                .Where(image =>
                    image.NormalisedBaseStem.Equals(id, StringComparison.OrdinalIgnoreCase)
                    || image.NormalisedBaseStem.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(image => SideOrder(image.Side))
                .ThenBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var front = matches.Where(image => image.Side is "Front" or "Primary").ToList();
            var back = matches.Where(image => image.Side is "Back" or "Reverse").ToList();
            var unidentified = matches.Where(image => image.Side == "Unspecified").ToList();

            string structure;
            string status;

            if (matches.Count == 0)
            {
                structure = "Missing";
                status = "Missing";
            }
            else if (HasRecognisedPair(front, back, unidentified, matches))
            {
                structure = "DoubleSided";
                status = matches.Count <= 2 ? "Matched" : "Ambiguous";
            }
            else
            {
                structure = "SingleSided";
                status = matches.Count == 1 ? "Matched" : "Ambiguous";
            }

            result.Add(new OfficialUpgradeArtworkEntry
            {
                Id = upgrade.Id,
                Name = upgrade.Name,
                Slot = upgrade.Slot,
                CardStructure = structure,
                Status = status,
                FrontImage = SelectFront(matches),
                BackImage = SelectBack(matches),
                Images = matches
            });
        }

        return result
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Slot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasRecognisedPair(
        IReadOnlyCollection<OfficialArtworkImage> front,
        IReadOnlyCollection<OfficialArtworkImage> back,
        IReadOnlyCollection<OfficialArtworkImage> unidentified,
        IReadOnlyCollection<OfficialArtworkImage> all)
    {
        if (front.Count == 1 && back.Count == 1)
            return true;

        if (back.Count == 1 && unidentified.Count == 1 && all.Count == 2)
            return true;

        return false;
    }

    private static OfficialArtworkImage? SelectFront(IReadOnlyList<OfficialArtworkImage> matches) =>
        matches.FirstOrDefault(image => image.Side == "Front")
        ?? matches.FirstOrDefault(image => image.Side == "Primary")
        ?? matches.FirstOrDefault(image => image.Side == "Unspecified");

    private static OfficialArtworkImage? SelectBack(IReadOnlyList<OfficialArtworkImage> matches) =>
        matches.FirstOrDefault(image => image.Side == "Back")
        ?? matches.FirstOrDefault(image => image.Side == "Reverse");

    private static bool MatchesPilot(OfficialArtworkImage image, FirstEditionDataPilot pilot)
    {
        var pilotId = Normalise(pilot.Id);
        var pilotName = Normalise(pilot.Name);
        var shipId = Normalise(pilot.ShipId);
        var faction = Normalise(pilot.Faction);

        if (!string.IsNullOrWhiteSpace(image.FactionFolder)
            && !FactionEquivalent(Normalise(image.FactionFolder), faction))
            return false;

        if (!string.IsNullOrWhiteSpace(image.EntityFolder)
            && !EquivalentToken(Normalise(image.EntityFolder), shipId))
            return false;

        return image.NormalisedBaseStem.Equals(pilotId, StringComparison.OrdinalIgnoreCase)
            || image.NormalisedBaseStem.Equals(pilotName, StringComparison.OrdinalIgnoreCase)
            || (image.NormalisedBaseStem.Equals(shipId, StringComparison.OrdinalIgnoreCase)
                && image.Side is "Fore" or "Aft");
    }

    private static bool FactionEquivalent(string left, string right)
    {
        static string Canonical(string value) => value switch
        {
            "rebel" or "rebels" or "rebelalliance" => "rebelalliance",
            "empire" or "imperial" or "galacticempire" => "galacticempire",
            "scum" or "scumandvillainy" => "scumandvillainy",
            "firstorder" or "fo" => "firstorder",
            _ => value
        };

        return Canonical(left).Equals(Canonical(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool EquivalentToken(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase)
        || left.Replace("class", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Equals(right.Replace("class", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);

    private static string ClassifyPilotStructure(IReadOnlyCollection<OfficialArtworkImage> images)
    {
        if (images.Any(image => image.Side == "Fore") || images.Any(image => image.Side == "Aft"))
            return "ForeAft";

        return images.Count > 0 ? "SingleCard" : "Missing";
    }

    private static List<OfficialArtworkImage> ScanImages(string sourceRoot, string imagesRoot)
    {
        if (!Directory.Exists(imagesRoot))
            return new List<OfficialArtworkImage>();

        return Directory.EnumerateFiles(imagesRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .Select(path => CreateImage(sourceRoot, imagesRoot, path))
            .OrderBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OfficialArtworkImage CreateImage(string sourceRoot, string imagesRoot, string path)
    {
        var relativeToImages = Path.GetRelativePath(imagesRoot, path);
        var parts = relativeToImages.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stem = Path.GetFileNameWithoutExtension(path);
        var (baseStem, side) = SplitSide(stem);

        return new OfficialArtworkImage
        {
            AssetKey = StableId(Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')),
            RepositoryPath = Path.GetRelativePath(Path.GetDirectoryName(sourceRoot) ?? sourceRoot, path).Replace('\\', '/'),
            SourceRelativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
            FactionFolder = parts.Length >= 3 ? parts[0] : string.Empty,
            EntityFolder = parts.Length >= 3 ? parts[1] : parts.Length >= 2 ? parts[0] : string.Empty,
            FileName = Path.GetFileName(path),
            BaseStem = baseStem,
            NormalisedBaseStem = Normalise(baseStem),
            Side = side
        };
    }

    private static (string BaseStem, string Side) SplitSide(string stem)
    {
        var match = SideSuffix.Match(stem);
        if (!match.Success)
            return (stem, "Unspecified");

        var sideToken = Normalise(match.Groups["side"].Value);
        var side = sideToken switch
        {
            "front" or "sidea" => "Front",
            "back" or "rear" or "sideb" => "Back",
            "reverse" => "Reverse",
            "fore" => "Fore",
            "aft" => "Aft",
            _ => "Unspecified"
        };

        return (stem[..match.Index], side);
    }

    private static List<OfficialArtworkImage> FindUnassigned(
        IReadOnlyList<OfficialArtworkImage> all,
        IEnumerable<OfficialArtworkImage> assigned)
    {
        var assignedPaths = assigned
            .Select(image => image.SourceRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return all
            .Where(image => !assignedPaths.Contains(image.SourceRelativePath))
            .ToList();
    }

    private static int SideOrder(string side) => side switch
    {
        "Front" or "Primary" or "Fore" => 0,
        "Back" or "Reverse" or "Aft" => 1,
        _ => 2
    };

    private static string Normalise(string value) =>
        new((value ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

    private static string? ResolveExplicitXWingData(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            return null;

        var candidate = Path.GetFullPath(args[1]);
        return FirstEditionDataSourceResolver.LooksLikeDataSource(candidate)
            ? candidate
            : null;
    }

    private static string ResolveOutput(string repositoryRoot, string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[index + 1]);
        }

        return Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11e", "official-artwork-manifest");
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static void WritePilotCsv(string path, IReadOnlyList<OfficialPilotArtworkEntry> entries)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Id,Name,ShipId,Faction,CardStructure,Status,ImageCount,Images");
        foreach (var entry in entries)
            writer.WriteLine(string.Join(',', Csv(entry.Id), Csv(entry.Name), Csv(entry.ShipId), Csv(entry.Faction), Csv(entry.CardStructure), Csv(entry.Status), entry.Images.Count, Csv(string.Join(';', entry.Images.Select(image => image.SourceRelativePath)))));
    }

    private static void WriteUpgradeCsv(string path, IReadOnlyList<OfficialUpgradeArtworkEntry> entries)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Id,Name,Slot,CardStructure,Status,ImageCount,FrontImage,BackImage,Images");
        foreach (var entry in entries)
            writer.WriteLine(string.Join(',', Csv(entry.Id), Csv(entry.Name), Csv(entry.Slot), Csv(entry.CardStructure), Csv(entry.Status), entry.Images.Count, Csv(entry.FrontImage?.SourceRelativePath), Csv(entry.BackImage?.SourceRelativePath), Csv(string.Join(';', entry.Images.Select(image => image.SourceRelativePath)))));
    }

    private static void WriteMarkdown(string path, OfficialArtworkManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Official Artwork Manifest");
        writer.WriteLine();
        writer.WriteLine($"Generated: `{manifest.GeneratedUtc:O}`  ");
        writer.WriteLine($"Data source: `{manifest.XWingDataRoot}`  ");
        writer.WriteLine($"Image source: `{manifest.XWingDataImagesRoot}`");
        writer.WriteLine();
        writer.WriteLine("## Summary");
        writer.WriteLine();
        writer.WriteLine($"- Pilot definitions: **{manifest.PilotDefinitionCount}**");
        writer.WriteLine($"- Pilot images: **{manifest.PilotImageCount}**");
        writer.WriteLine($"- Upgrade definitions: **{manifest.UpgradeDefinitionCount}**");
        writer.WriteLine($"- Upgrade images: **{manifest.UpgradeImageCount}**");
        writer.WriteLine($"- Double-sided upgrade cards: **{manifest.UpgradeEntries.Count(entry => entry.CardStructure == "DoubleSided")}**");
        writer.WriteLine($"- Fore/aft Epic card entries: **{manifest.PilotEntries.Count(entry => entry.CardStructure == "ForeAft")}**");
        writer.WriteLine($"- Unassigned pilot images: **{manifest.UnassignedPilotImages.Count}**");
        writer.WriteLine($"- Unassigned upgrade images: **{manifest.UnassignedUpgradeImages.Count}**");
        writer.WriteLine();
        writer.WriteLine("## Double-sided upgrades");
        writer.WriteLine();
        var doubleSided = manifest.UpgradeEntries.Where(entry => entry.CardStructure == "DoubleSided").ToList();
        if (doubleSided.Count == 0)
            writer.WriteLine("None automatically recognised.");
        else
            foreach (var entry in doubleSided)
                writer.WriteLine($"- **{entry.Name}** — `{entry.FrontImage?.SourceRelativePath}` / `{entry.BackImage?.SourceRelativePath}`");
        writer.WriteLine();
        writer.WriteLine("## Epic fore/aft cards");
        writer.WriteLine();
        var foreAft = manifest.PilotEntries.Where(entry => entry.CardStructure == "ForeAft").ToList();
        if (foreAft.Count == 0)
            writer.WriteLine("None.");
        else
            foreach (var entry in foreAft)
                writer.WriteLine($"- **{entry.Name}** — {string.Join(", ", entry.Images.Select(image => $"`{image.SourceRelativePath}`"))}");
        writer.WriteLine();
        writer.WriteLine("Ambiguous and unassigned images remain in the JSON/CSV reports for review. The command never merges images silently.");
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  build-official-artwork-manifest <first-edition-repository> [xwing-data-folder] [--output <folder>]");
    }
}

public sealed class OfficialArtworkManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Repository { get; init; } = string.Empty;
    public string XWingDataRoot { get; init; } = string.Empty;
    public string XWingDataImagesRoot { get; init; } = string.Empty;
    public int PilotDefinitionCount { get; init; }
    public int UpgradeDefinitionCount { get; init; }
    public int PilotImageCount { get; init; }
    public int UpgradeImageCount { get; init; }
    public List<OfficialPilotArtworkEntry> PilotEntries { get; init; } = new();
    public List<OfficialUpgradeArtworkEntry> UpgradeEntries { get; init; } = new();
    public List<OfficialArtworkImage> UnassignedPilotImages { get; init; } = new();
    public List<OfficialArtworkImage> UnassignedUpgradeImages { get; init; } = new();
}

public sealed class OfficialPilotArtworkEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string CardStructure { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public List<OfficialArtworkImage> Images { get; init; } = new();
}

public sealed class OfficialUpgradeArtworkEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public string CardStructure { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public OfficialArtworkImage? FrontImage { get; init; }
    public OfficialArtworkImage? BackImage { get; init; }
    public List<OfficialArtworkImage> Images { get; init; } = new();
}

public sealed class OfficialArtworkImage
{
    public string AssetKey { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string SourceRelativePath { get; init; } = string.Empty;
    public string FactionFolder { get; init; } = string.Empty;
    public string EntityFolder { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string BaseStem { get; init; } = string.Empty;
    public string NormalisedBaseStem { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
}
