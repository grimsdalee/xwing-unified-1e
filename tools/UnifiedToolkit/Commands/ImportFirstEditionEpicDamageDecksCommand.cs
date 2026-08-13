using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>Imports the complete First Edition Epic ship damage decks without altering scanned artwork.</summary>
public static class ImportFirstEditionEpicDamageDecksCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var source = Path.GetFullPath(Option(args, "--source") ?? Path.Combine(repository,
                "assets", "source", "xwing-data", "images", "damage-decks"));
            var destination = Path.GetFullPath(Option(args, "--destination") ?? Path.Combine(repository,
                "assets", "source", "unified1e", "damage-decks"));
            var manifestPath = Path.GetFullPath(Option(args, "--manifest") ?? Path.Combine(repository,
                "assets", "source", "unified1e", "reference", "cards", "epic-damage-decks.json"));
            RequireDirectory(source, "Epic damage-deck source");

            var sections = Definitions();
            var imported = 0;
            var unchanged = 0;
            var manifestSections = new List<EpicDamageDeckManifestSection>();

            foreach (var section in sections)
            {
                var sourceFolder = Path.Combine(source, section.ShipId);
                var destinationFolder = Path.Combine(destination, section.ShipId, section.Section);
                RequireDirectory(sourceFolder, $"{section.ShipName} scan folder");
                Directory.CreateDirectory(destinationFolder);

                var backSource = Path.Combine(sourceFolder, $"back-{section.Section}.png");
                var backDestination = Path.Combine(destinationFolder, "back.png");
                var back = CopyArtwork(repository, backSource, backDestination, ref imported, ref unchanged);
                var cards = new List<EpicDamageDeckManifestCard>();

                foreach (var expected in section.Cards)
                {
                    var slug = Slug(expected.Name);
                    var sectionSpecific = Path.Combine(sourceFolder, $"{slug}-{section.Section}.png");
                    var shared = Path.Combine(sourceFolder, $"{slug}.png");
                    var faceSource = File.Exists(sectionSpecific) ? sectionSpecific : shared;
                    if (!File.Exists(faceSource))
                        throw new FileNotFoundException($"{section.ShipName} {section.Section} artwork missing for {expected.Name}. Expected '{Path.GetFileName(sectionSpecific)}' or '{Path.GetFileName(shared)}'.");
                    var faceDestination = Path.Combine(destinationFolder, $"{slug}.png");
                    var face = CopyArtwork(repository, faceSource, faceDestination, ref imported, ref unchanged);
                    cards.Add(new EpicDamageDeckManifestCard
                    {
                        Name = expected.Name,
                        Xws = slug.Replace("-", ""),
                        Quantity = expected.Quantity,
                        SourceRepositoryPath = Relative(repository, faceSource),
                        FaceRepositoryPath = Relative(repository, faceDestination),
                        Width = face.Width,
                        Height = face.Height,
                        Sha256 = face.Sha256
                    });
                }

                manifestSections.Add(new EpicDamageDeckManifestSection
                {
                    ShipId = section.ShipId,
                    ShipName = section.ShipName,
                    Section = section.Section,
                    PhysicalCardCount = cards.Sum(card => card.Quantity),
                    UniqueFaceCount = cards.Count,
                    BackRepositoryPath = Relative(repository, backDestination),
                    BackWidth = back.Width,
                    BackHeight = back.Height,
                    BackSha256 = back.Sha256,
                    Cards = cards
                });
            }

            if (manifestSections.Count != 10 || manifestSections.Sum(section => section.PhysicalCardCount) != 100)
                throw new InvalidDataException("Expected 10 Epic section decks and 100 physical cards.");

            var manifest = new EpicDamageDeckManifest
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceFolder = Relative(repository, source),
                DestinationFolder = Relative(repository, destination),
                ShipCount = manifestSections.Select(section => section.ShipId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                SectionDeckCount = manifestSections.Count,
                PhysicalCardCount = manifestSections.Sum(section => section.PhysicalCardCount),
                UniqueFaceAssignmentCount = manifestSections.Sum(section => section.UniqueFaceCount),
                Sections = manifestSections
            };
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Epic Damage-Deck Import");
            Console.WriteLine("=====================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:               {repository}");
            Console.WriteLine($"Source:                   {source}");
            Console.WriteLine($"Destination:              {destination}");
            Console.WriteLine($"Epic ships:               {manifest.ShipCount}");
            Console.WriteLine($"Section decks:            {manifest.SectionDeckCount}");
            Console.WriteLine($"Physical cards:           {manifest.PhysicalCardCount}");
            Console.WriteLine($"Unique face assignments:  {manifest.UniqueFaceAssignmentCount}");
            Console.WriteLine($"Imported or updated:      {imported}");
            Console.WriteLine($"Unchanged:                {unchanged}");
            Console.WriteLine($"Manifest:                 {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Epic damage decks imported successfully. PNG files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition Epic damage-deck import failed: {exception.Message}");
            return 1;
        }
    }

    private static ArtworkInfo CopyArtwork(string repository, string source, string destination,
        ref int imported, ref int unchanged)
    {
        RequireFile(source, "Damage-deck artwork");
        using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidDataException($"PNG could not be decoded: {source}");
        var bytes = File.ReadAllBytes(source);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (bitmap.Width != 484 || bitmap.Height != 744)
            throw new InvalidDataException($"{Relative(repository, source)} is {bitmap.Width}x{bitmap.Height}; all Epic damage-deck scans must be 484x744.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(destination)).SequenceEqual(SHA256.HashData(bytes)))
            unchanged++;
        else
        {
            File.Copy(source, destination, true);
            imported++;
        }
        return new ArtworkInfo(bitmap.Width, bitmap.Height, hash);
    }

    private static List<EpicSectionDefinition> Definitions() => new()
    {
        Section("gr75mediumtransport", "GR-75 Medium Transport", "fore", Card("Secondary Drive Failure"), Card("Broadcast Malfunction"), Card("Direct Hit", 4), Card("Hull Breach", 2), Card("Damaged Stabilizers", 2)),
        Section("gr75mediumtransport", "GR-75 Medium Transport", "aft", Card("Reactor Cowl Rupture"), Card("Projector Power Failure"), Card("Command Pod Casualties", 2), Card("Reactor Leak", 3), Card("Engine Damage", 3)),
        Section("cr90corvette", "CR90 Corvette", "fore", Card("Comms Failure"), Card("Deck Breach"), Card("Direct Hit", 2), Card("Life Support Failure"), Card("Scrambled Scopes"), Card("Secondary Drive Failure"), Card("Tracking Misalignment"), Card("Weapon Damaged", 2)),
        Section("cr90corvette", "CR90 Corvette", "aft", Card("Deck Breach"), Card("Grid Overload", 2), Card("Life Support Failure"), Card("Power Plant Failure"), Card("Projector Power Failure"), Card("Reactor Leak", 2), Card("Structural Collapse", 2)),
        Section("raiderclasscorvette", "Raider-class Corvette", "fore", Card("Deck Breach"), Card("Direct Hit", 2), Card("Power Plant Failure"), Card("Projector Power Failure"), Card("Reactor Leak", 2), Card("Secondary Drive Failure"), Card("Tracking Misalignment"), Card("Weapon Damaged")),
        Section("raiderclasscorvette", "Raider-class Corvette", "aft", Card("Command Deck Breach"), Card("Comms Failure"), Card("Deck Breach"), Card("Life Support Failure"), Card("Misfiring Thrusters", 2), Card("Scrambled Scopes"), Card("Structural Collapse", 2), Card("Weapon Damaged")),
        Section("gozanticlasscruiser", "Gozanti-class Cruiser", "fore", Card("Damaged Docking Clamp", 3), Card("Hull Breach", 2), Card("Comms Failure"), Card("Secondary Drive Failure"), Card("Scrambled Scopes", 2), Card("Viewport Rupture")),
        Section("gozanticlasscruiser", "Gozanti-class Cruiser", "aft", Card("Damaged Stabilizers", 2), Card("Weapons Offline"), Card("Reactor Leak", 2), Card("Reactor Cowl Rupture"), Card("Projector Power Failure"), Card("Damaged Docking Clamp", 3)),
        Section("croccruiser", "C-ROC Cruiser", "fore", Card("Hull Breach", 2), Card("Secondary Drive Failure", 2), Card("Scrambled Scopes", 2), Card("Viewport Rupture", 2), Card("Spilled Cargo", 2)),
        Section("croccruiser", "C-ROC Cruiser", "aft", Card("Damaged Stabilizers", 2), Card("Weapons Offline"), Card("Reactor Leak", 2), Card("Projector Power Failure"), Card("Reactor Cowl Rupture"), Card("Damaged Dish"), Card("Spilled Cargo", 2))
    };

    private static EpicSectionDefinition Section(string shipId, string shipName, string section, params EpicCardDefinition[] cards) => new(shipId, shipName, section, cards.ToList());
    private static EpicCardDefinition Card(string name, int quantity = 1) => new(name, quantity);
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(new[] { ' ', '-', '/', '\\', '(', ')' }, StringSplitOptions.RemoveEmptyEntries));
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit import-first-edition-epic-damage-decks <repository> [--source <folder>] [--destination <folder>] [--manifest <file>]");
    private sealed record ArtworkInfo(int Width, int Height, string Sha256);
    private sealed record EpicSectionDefinition(string ShipId, string ShipName, string Section, List<EpicCardDefinition> Cards);
    private sealed record EpicCardDefinition(string Name, int Quantity);
}

public sealed class EpicDamageDeckManifest
{
    public int SchemaVersion { get; init; } public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceFolder { get; init; } = ""; public string DestinationFolder { get; init; } = "";
    public int ShipCount { get; init; } public int SectionDeckCount { get; init; } public int PhysicalCardCount { get; init; }
    public int UniqueFaceAssignmentCount { get; init; }
    public List<EpicDamageDeckManifestSection> Sections { get; init; } = new();
}
public sealed class EpicDamageDeckManifestSection
{
    public string ShipId { get; init; } = ""; public string ShipName { get; init; } = ""; public string Section { get; init; } = "";
    public int PhysicalCardCount { get; init; } public int UniqueFaceCount { get; init; }
    public string BackRepositoryPath { get; init; } = ""; public int BackWidth { get; init; } public int BackHeight { get; init; }
    public string BackSha256 { get; init; } = ""; public List<EpicDamageDeckManifestCard> Cards { get; init; } = new();
}
public sealed class EpicDamageDeckManifestCard
{
    public string Name { get; init; } = ""; public string Xws { get; init; } = ""; public int Quantity { get; init; }
    public string SourceRepositoryPath { get; init; } = ""; public string FaceRepositoryPath { get; init; } = "";
    public int Width { get; init; } public int Height { get; init; } public string Sha256 { get; init; } = "";
}
