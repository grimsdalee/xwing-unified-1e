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

    private static readonly IReadOnlyDictionary<string, UpgradeArtworkPair> DoubleSidedUpgradePairs =
        new Dictionary<string, UpgradeArtworkPair>(StringComparer.OrdinalIgnoreCase)
        {
            ["adaptability"] = new("adaptability-increase", "adaptability-decrease"),
            ["arccaster"] = new("arc-caster", "arc-caster-recharging"),
            ["intensity"] = new("intensity", "intensity-exhausted"),
            ["pivotwing"] = new("pivot-wing-attack", "pivot-wing-landing"),
            ["servomotorsfoils"] = new("servomotor-s-foils-attack", "servomotor-s-foils-closed")
        };

    private static readonly IReadOnlyDictionary<string, string> UpgradePrintingArtwork =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["millenniumfalcon-swx57"] = "millennium-falcon-hotr"
        };


    private static readonly IReadOnlyDictionary<string, string> PilotPrintingArtwork =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Heroes of the Resistance Poe Dameron is a distinct PS9 pilot
            // printing, not alternate artwork for the Core Set PS8 pilot.
            ["poedameron-swx57"] = "poe-dameron-hotr"
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
            Console.WriteLine($"Epic fore section cards:{pilotEntries.Count(entry => entry.CardStructure == "EpicFore"),4}");
            Console.WriteLine($"Epic aft section cards: {pilotEntries.Count(entry => entry.CardStructure == "EpicAft"),4}");
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
            .GroupBy(
                pilot => $"{Normalise(pilot.Id)}|{Normalise(pilot.ShipId)}|{Normalise(pilot.Faction)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var result = new List<OfficialPilotArtworkEntry>();

        foreach (var pilot in definitions)
        {
            var matches = MatchPilotImages(pilot, images)
                .OrderBy(image => SideOrder(image.Side))
                .ThenBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var isEpicSection = TryGetEpicSection(pilot, out var section);

            result.Add(new OfficialPilotArtworkEntry
            {
                Id = pilot.Id,
                Name = pilot.Name,
                ShipId = pilot.ShipId,
                Faction = pilot.Faction,
                CardStructure = isEpicSection
                    ? $"Epic{section}"
                    : "SingleCard",
                Status = matches.Count switch
                {
                    0 => "Missing",
                    1 => "Matched",
                    _ => "Ambiguous"
                },
                Images = matches
            });
        }

        return result
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.ShipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<OfficialArtworkImage> MatchPilotImages(
        FirstEditionDataPilot pilot,
        IReadOnlyList<OfficialArtworkImage> images)
    {
        var pilotIdentity = Normalise(pilot.Id);
        var expectedStem = PilotPrintingArtwork.TryGetValue(
            pilot.Id,
            out var printingStem)
            ? Normalise(printingStem)
            : pilotIdentity.Equals("poedameronswx57", StringComparison.OrdinalIgnoreCase)
                ? Normalise("poe-dameron-hotr")
                : pilotIdentity;

        var exact = images.Where(image =>
            PilotFolderMatches(image, pilot)
            && image.NormalisedBaseStem.Equals(
                expectedStem,
                StringComparison.OrdinalIgnoreCase));

        if (exact.Any())
            return exact;

        var normalisedName = Normalise(pilot.Name);
        var byName = images.Where(image =>
            PilotFolderMatches(image, pilot)
            && image.NormalisedBaseStem.Equals(
                normalisedName,
                StringComparison.OrdinalIgnoreCase));

        if (byName.Any())
            return byName;

        if (TryGetEpicSection(pilot, out var section))
        {
            return images.Where(image =>
                PilotFolderMatches(image, pilot)
                && image.Side.Equals(section, StringComparison.OrdinalIgnoreCase));
        }

        return Array.Empty<OfficialArtworkImage>();
    }

    private static bool PilotFolderMatches(
        OfficialArtworkImage image,
        FirstEditionDataPilot pilot)
    {
        var faction = Normalise(pilot.Faction);
        var shipId = NormaliseEpicSectionShipId(pilot.ShipId);

        if (!string.IsNullOrWhiteSpace(image.FactionFolder)
            && !FactionEquivalent(Normalise(image.FactionFolder), faction))
            return false;

        return string.IsNullOrWhiteSpace(image.EntityFolder)
            || EquivalentToken(Normalise(image.EntityFolder), shipId);
    }

    private static string NormaliseEpicSectionShipId(string shipId)
    {
        var value = Normalise(shipId);

        return value switch
        {
            "cr90corvettefore" or "cr90corvetteaft" => "cr90corvette",
            "raiderclasscorvettefore" or "raiderclasscorvetteaft" => "raiderclasscorvette",
            _ => value
        };
    }

    private static bool TryGetEpicSection(
        FirstEditionDataPilot pilot,
        out string section)
    {
        // Do not use a generic EndsWith("aft") test here:
        // ordinary pilot identities such as "Backdraft" also end in "aft".
        // Epic section cards are explicit, finite semantic identities.
        var id = Normalise(pilot.Id);

        if (id is "cr90corvettefore" or "raiderclasscorvettefore")
        {
            section = "Fore";
            return true;
        }

        if (id is "cr90corvetteaft" or "raiderclasscorvetteaft")
        {
            section = "Aft";
            return true;
        }

        section = string.Empty;
        return false;
    }

    private static List<OfficialUpgradeArtworkEntry> BuildUpgradeEntries(
        IReadOnlyList<FirstEditionDataUpgrade> upgrades,
        IReadOnlyList<OfficialArtworkImage> images)
    {
        var result = new List<OfficialUpgradeArtworkEntry>();

        foreach (var upgrade in upgrades)
        {
            var normalisedId = Normalise(upgrade.Id);
            var slot = Normalise(upgrade.Slot);

            if (DoubleSidedUpgradePairs.TryGetValue(normalisedId, out var pair))
            {
                result.Add(BuildDoubleSidedEntry(upgrade, images, pair));
                continue;
            }

            var expectedStem = UpgradePrintingArtwork.TryGetValue(
                upgrade.Id,
                out var printingStem)
                ? Normalise(printingStem)
                : normalisedId;

            var matches = images
                .Where(image =>
                    SlotEquivalent(image.EntityFolder, slot)
                    && image.NormalisedBaseStem.Equals(
                        expectedStem,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Some older source definitions use an unsuffixed semantic ID while
            // the image stem is derived from the display name. Use this only
            // when exact ID + slot matching produced no result.
            if (matches.Count == 0)
            {
                var normalisedName = Normalise(upgrade.Name);
                matches = images
                    .Where(image =>
                        SlotEquivalent(image.EntityFolder, slot)
                        && image.NormalisedBaseStem.Equals(
                            normalisedName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            result.Add(new OfficialUpgradeArtworkEntry
            {
                Id = upgrade.Id,
                Name = upgrade.Name,
                Slot = upgrade.Slot,
                CardStructure = matches.Count == 0 ? "Missing" : "SingleSided",
                Status = matches.Count switch
                {
                    0 => "Missing",
                    1 => "Matched",
                    _ => "Ambiguous"
                },
                FrontImage = matches.Count == 1 ? matches[0] : null,
                BackImage = null,
                Images = matches
            });
        }

        return result
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Slot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OfficialUpgradeArtworkEntry BuildDoubleSidedEntry(
        FirstEditionDataUpgrade upgrade,
        IReadOnlyList<OfficialArtworkImage> images,
        UpgradeArtworkPair pair)
    {
        var slot = Normalise(upgrade.Slot);
        var frontStem = Normalise(pair.FrontStem);
        var backStem = Normalise(pair.BackStem);

        var frontMatches = images
            .Where(image =>
                SlotEquivalent(image.EntityFolder, slot)
                && image.NormalisedBaseStem.Equals(
                    frontStem,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var backMatches = images
            .Where(image =>
                SlotEquivalent(image.EntityFolder, slot)
                && image.NormalisedBaseStem.Equals(
                    backStem,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(image => image.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var all = frontMatches
            .Concat(backMatches)
            .GroupBy(
                image => image.SourceRelativePath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var matched = frontMatches.Count == 1 && backMatches.Count == 1;
        var missing = frontMatches.Count == 0 || backMatches.Count == 0;

        return new OfficialUpgradeArtworkEntry
        {
            Id = upgrade.Id,
            Name = upgrade.Name,
            Slot = upgrade.Slot,
            CardStructure = "DoubleSided",
            Status = matched
                ? "Matched"
                : missing
                    ? "Missing"
                    : "Ambiguous",
            FrontImage = frontMatches.Count == 1
                ? MarkSide(frontMatches[0], "Front")
                : null,
            BackImage = backMatches.Count == 1
                ? MarkSide(backMatches[0], "Back")
                : null,
            Images = all
                .Select(image =>
                    image.SourceRelativePath.Equals(
                        frontMatches.FirstOrDefault()?.SourceRelativePath,
                        StringComparison.OrdinalIgnoreCase)
                        ? MarkSide(image, "Front")
                        : image.SourceRelativePath.Equals(
                            backMatches.FirstOrDefault()?.SourceRelativePath,
                            StringComparison.OrdinalIgnoreCase)
                            ? MarkSide(image, "Back")
                            : image)
                .OrderBy(image => SideOrder(image.Side))
                .ToList()
        };
    }

    private static OfficialArtworkImage MarkSide(
        OfficialArtworkImage image,
        string side) =>
        new()
        {
            AssetKey = image.AssetKey,
            RepositoryPath = image.RepositoryPath,
            SourceRelativePath = image.SourceRelativePath,
            FactionFolder = image.FactionFolder,
            EntityFolder = image.EntityFolder,
            FileName = image.FileName,
            BaseStem = image.BaseStem,
            NormalisedBaseStem = image.NormalisedBaseStem,
            Side = side
        };

    private static bool SlotEquivalent(string imageFolder, string upgradeSlot)
    {
        var folder = Normalise(imageFolder);
        var slot = Normalise(upgradeSlot);

        if (folder.Equals(slot, StringComparison.OrdinalIgnoreCase))
            return true;

        return (folder, slot) switch
        {
            ("elite", "talent") or ("talent", "elite") => true,
            ("astromech", "amd") or ("amd", "astromech") => true,
            ("salvagedastromech", "salvagedastromech") => true,
            _ => false
        };
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
        writer.WriteLine($"- Epic fore section cards: **{manifest.PilotEntries.Count(entry => entry.CardStructure == "EpicFore")}**");
        writer.WriteLine($"- Epic aft section cards: **{manifest.PilotEntries.Count(entry => entry.CardStructure == "EpicAft")}**");
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
        writer.WriteLine("## Epic section cards");
        writer.WriteLine();
        var epicSections = manifest.PilotEntries
            .Where(entry => entry.CardStructure is "EpicFore" or "EpicAft")
            .ToList();
        if (epicSections.Count == 0)
            writer.WriteLine("None.");
        else
            foreach (var entry in epicSections)
                writer.WriteLine($"- **{entry.Name}** ({entry.CardStructure[4..]}) — {string.Join(", ", entry.Images.Select(image => $"`{image.SourceRelativePath}`"))}");
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

public sealed record UpgradeArtworkPair(
    string FrontStem,
    string BackStem);

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
