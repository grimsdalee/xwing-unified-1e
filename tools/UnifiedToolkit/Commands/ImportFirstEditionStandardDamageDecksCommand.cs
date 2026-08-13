using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.Commands;

/// <summary>Imports the First Edition Core Set and The Force Awakens Core Set damage decks without altering scanned artwork.</summary>
public static class ImportFirstEditionStandardDamageDecksCommand
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
            var data = Path.GetFullPath(Option(args, "--data") ?? Path.Combine(repository,
                "source", "xwing-data", "data"));
            var destination = Path.GetFullPath(Option(args, "--destination") ?? Path.Combine(repository,
                "assets", "source", "unified1e", "damage-decks"));
            var manifestPath = Path.GetFullPath(Option(args, "--manifest") ?? Path.Combine(repository,
                "assets", "source", "unified1e", "reference", "cards", "standard-damage-decks.json"));

            RequireDirectory(source, "Damage-deck scan source");
            RequireDirectory(data, "xwing-data definitions");

            var pendingDecks = Definitions().Select(definition =>
                PrepareDeck(repository, source, data, destination, definition)).ToList();

            if (pendingDecks.Count != 2 || pendingDecks.Any(deck => deck.Cards.Count != 14) ||
                pendingDecks.Any(deck => deck.PhysicalCardCount != 33))
                throw new InvalidDataException("Expected two standard damage decks with 14 unique faces and 33 physical cards each.");

            var imported = 0;
            var unchanged = 0;
            foreach (var pendingDeck in pendingDecks)
            {
                CopyArtwork(pendingDeck.BackSourcePath, pendingDeck.BackDestinationPath, ref imported, ref unchanged);
                foreach (var pendingCard in pendingDeck.PendingCards)
                    CopyArtwork(pendingCard.SourcePath, pendingCard.DestinationPath, ref imported, ref unchanged);
            }

            var manifest = new StandardDamageDeckManifest
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceFolder = Relative(repository, source),
                DataFolder = Relative(repository, data),
                DestinationFolder = Relative(repository, destination),
                DeckCount = pendingDecks.Count,
                PhysicalCardCount = pendingDecks.Sum(deck => deck.PhysicalCardCount),
                UniqueFaceCount = pendingDecks.Sum(deck => deck.UniqueFaceCount),
                Decks = pendingDecks.Select(deck => deck.ToManifest()).ToList()
            };

            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Standard Damage-Deck Import");
            Console.WriteLine("========================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:             {repository}");
            Console.WriteLine($"Source:                 {source}");
            Console.WriteLine($"Destination:            {destination}");
            Console.WriteLine($"Damage decks:           {manifest.DeckCount}");
            Console.WriteLine($"Physical cards:         {manifest.PhysicalCardCount}");
            Console.WriteLine($"Unique faces:           {manifest.UniqueFaceCount}");
            Console.WriteLine($"Imported or updated:    {imported}");
            Console.WriteLine($"Unchanged:              {unchanged}");
            Console.WriteLine($"Manifest:               {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
            var build = new KnowledgeBaseBuilder().Build(repository, refreshCatalogue: true);
            Console.WriteLine($"Asset files:            {build.FileCount}");
            Console.WriteLine($"Unique assets:          {build.UniqueAssetCount}");
            Console.WriteLine($"Duplicate files:        {build.DuplicateFileCount}");
            Console.WriteLine($"Unavailable sources:    {build.UnavailableSourceCount}");
            Console.WriteLine($"Validation errors:      {build.ErrorCount}");
            Console.WriteLine($"Validation warnings:    {build.WarningCount}");
            Console.WriteLine($"Knowledge base:         {build.OutputRoot}");
            Console.WriteLine();

            if (build.ErrorCount > 0)
            {
                Console.Error.WriteLine("Damage decks were imported, but the Unified Knowledge Base contains validation errors.");
                return 2;
            }

            Console.WriteLine("Standard damage decks imported successfully. PNG files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition standard damage-deck import failed: {exception.Message}");
            return 1;
        }
    }

    private static PendingStandardDamageDeck PrepareDeck(
        string repository,
        string sourceRoot,
        string dataRoot,
        string destinationRoot,
        StandardDeckDefinition definition)
    {
        var sourceFolder = Path.Combine(sourceRoot, definition.Id);
        var destinationFolder = Path.Combine(destinationRoot, definition.Id);
        var dataPath = Path.Combine(dataRoot, definition.DataFileName);
        RequireDirectory(sourceFolder, $"{definition.Name} scan folder");
        RequireFile(dataPath, $"{definition.Name} definition");

        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{Relative(repository, dataPath)} must contain a JSON array.");

        var pendingCards = new List<PendingStandardDamageCard>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = RequiredText(item, "name", dataPath);
            var image = RequiredText(item, "image", dataPath).Replace('/', Path.DirectorySeparatorChar);
            var amount = RequiredInt(item, "amount", dataPath);
            if (amount < 1)
                throw new InvalidDataException($"{definition.Name} card '{name}' has invalid amount {amount}.");

            var expectedPrefix = Path.Combine("damage-decks", definition.Id) + Path.DirectorySeparatorChar;
            if (!image.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{definition.Name} card '{name}' points outside its damage-deck folder: {image}.");

            var fileName = Path.GetFileName(image);
            var sourcePath = Path.Combine(sourceFolder, fileName);
            var destinationPath = Path.Combine(destinationFolder, fileName);
            var artwork = InspectArtwork(repository, sourcePath);
            pendingCards.Add(new PendingStandardDamageCard(
                sourcePath,
                destinationPath,
                new StandardDamageDeckManifestCard
                {
                    Name = name,
                    Xws = Path.GetFileNameWithoutExtension(fileName).Replace("-", "", StringComparison.Ordinal),
                    Type = OptionalText(item, "type"),
                    Quantity = amount,
                    SourceRepositoryPath = Relative(repository, sourcePath),
                    FaceRepositoryPath = Relative(repository, destinationPath),
                    Width = artwork.Width,
                    Height = artwork.Height,
                    Sha256 = artwork.Sha256
                }));
        }

        var duplicateFile = pendingCards.GroupBy(card => card.Manifest.FaceRepositoryPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateFile is not null)
            throw new InvalidDataException($"{definition.Name} definition assigns '{duplicateFile.Key}' more than once.");

        var backSource = Path.Combine(sourceFolder, "back.png");
        var backDestination = Path.Combine(destinationFolder, "back.png");
        var back = InspectArtwork(repository, backSource);

        var expectedPngs = pendingCards.Select(card => Path.GetFileName(card.SourcePath))
            .Append("back.png").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualPngs = Directory.EnumerateFiles(sourceFolder, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Where(fileName => fileName is not null)
            .Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedPngs.SetEquals(actualPngs))
        {
            var missing = expectedPngs.Except(actualPngs, StringComparer.OrdinalIgnoreCase).OrderBy(value => value);
            var unexpected = actualPngs.Except(expectedPngs, StringComparer.OrdinalIgnoreCase).OrderBy(value => value);
            throw new InvalidDataException($"{definition.Name} scan inventory mismatch. Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
        }

        return new PendingStandardDamageDeck(
            definition,
            backSource,
            backDestination,
            Relative(repository, backDestination),
            back,
            pendingCards);
    }

    private static ArtworkInfo InspectArtwork(string repository, string source)
    {
        RequireFile(source, "Damage-deck artwork");
        using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidDataException($"PNG could not be decoded: {source}");
        if (bitmap.Width != 484 || bitmap.Height != 744)
            throw new InvalidDataException($"{Relative(repository, source)} is {bitmap.Width}x{bitmap.Height}; all standard damage-deck scans must be 484x744.");
        return new ArtworkInfo(bitmap.Width, bitmap.Height,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
    }

    private static void CopyArtwork(string source, string destination, ref int imported, ref int unchanged)
    {
        var bytes = File.ReadAllBytes(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(destination)).SequenceEqual(SHA256.HashData(bytes)))
            unchanged++;
        else
        {
            File.Copy(source, destination, true);
            imported++;
        }
    }

    private static List<StandardDeckDefinition> Definitions() => new()
    {
        new("core", "Core Set", "damage-deck-core.js"),
        new("core-tfa", "The Force Awakens Core Set", "damage-deck-core-tfa.js")
    };

    private static string RequiredText(JsonElement item, string property, string path)
    {
        var value = OptionalText(item, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{path} contains a card without required '{property}'.");
        return value;
    }

    private static string OptionalText(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int RequiredInt(JsonElement item, string property, string path) =>
        item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"{path} contains a card without integer '{property}'.");

    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit import-first-edition-standard-damage-decks <repository> [--source <folder>] [--data <folder>] [--destination <folder>] [--manifest <file>]");

    private sealed record ArtworkInfo(int Width, int Height, string Sha256);
    private sealed record StandardDeckDefinition(string Id, string Name, string DataFileName);
    private sealed record PendingStandardDamageCard(string SourcePath, string DestinationPath, StandardDamageDeckManifestCard Manifest);
    private sealed record PendingStandardDamageDeck(
        StandardDeckDefinition Definition,
        string BackSourcePath,
        string BackDestinationPath,
        string BackRepositoryPath,
        ArtworkInfo Back,
        List<PendingStandardDamageCard> PendingCards)
    {
        public List<StandardDamageDeckManifestCard> Cards => PendingCards.Select(card => card.Manifest).ToList();
        public int PhysicalCardCount => PendingCards.Sum(card => card.Manifest.Quantity);
        public int UniqueFaceCount => PendingCards.Count;
        public StandardDamageDeckManifestDeck ToManifest() => new()
        {
            Id = Definition.Id,
            Name = Definition.Name,
            DataRepositoryPath = $"source/xwing-data/data/{Definition.DataFileName}",
            PhysicalCardCount = PhysicalCardCount,
            UniqueFaceCount = UniqueFaceCount,
            BackRepositoryPath = this.BackRepositoryPath,
            BackWidth = Back.Width,
            BackHeight = Back.Height,
            BackSha256 = Back.Sha256,
            Cards = Cards
        };
    }
}

public sealed class StandardDamageDeckManifest
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceFolder { get; init; } = "";
    public string DataFolder { get; init; } = "";
    public string DestinationFolder { get; init; } = "";
    public int DeckCount { get; init; }
    public int PhysicalCardCount { get; init; }
    public int UniqueFaceCount { get; init; }
    public List<StandardDamageDeckManifestDeck> Decks { get; init; } = new();
}

public sealed class StandardDamageDeckManifestDeck
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string DataRepositoryPath { get; init; } = "";
    public int PhysicalCardCount { get; init; }
    public int UniqueFaceCount { get; init; }
    public string BackRepositoryPath { get; init; } = "";
    public int BackWidth { get; init; }
    public int BackHeight { get; init; }
    public string BackSha256 { get; init; } = "";
    public List<StandardDamageDeckManifestCard> Cards { get; init; } = new();
}

public sealed class StandardDamageDeckManifestCard
{
    public string Name { get; init; } = "";
    public string Xws { get; init; } = "";
    public string Type { get; init; } = "";
    public int Quantity { get; init; }
    public string SourceRepositoryPath { get; init; } = "";
    public string FaceRepositoryPath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = "";
}
