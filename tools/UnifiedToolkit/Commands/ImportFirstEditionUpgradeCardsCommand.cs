using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Imports all official First Edition upgrade faces and catalogues their backs and conditions.</summary>
public static class ImportFirstEditionUpgradeCardsCommand
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
            var source = Path.GetFullPath(Option(args, "--source") ?? Path.Combine(
                repository, "assets", "source", "xwing-data", "images"));
            var destination = Path.GetFullPath(Option(args, "--destination") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "upgrade-cards"));
            var manifestPath = Path.GetFullPath(Option(args, "--manifest") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "reference", "cards", "upgrade-cards.json"));
            var dataPath = Path.Combine(repository, "source", "xwing-data", "data", "upgrades.js");
            var backRoot = Path.Combine(repository, "assets", "source", "unified1e", "upgrade-card-backs");
            var conditionRoot = Path.Combine(repository, "assets", "source", "unified1e", "condition-cards");

            RequireDirectory(repository, "Repository");
            RequireDirectory(source, "xwing-data artwork source");
            RequireDirectory(backRoot, "Canonical upgrade-card backs");
            RequireFile(dataPath, "xwing-data upgrade definitions");
            var upgrades = Load(dataPath);
            if (upgrades.Count != 367)
                throw new InvalidDataException($"Expected 367 First Edition upgrades but found {upgrades.Count}.");

            var imported = 0;
            var unchanged = 0;
            var legacyFilesRemoved = 0;
            var legacyFoldersRemoved = 0;
            var entries = new List<UpgradeCardManifestEntry>();
            var xwsCounts = upgrades.GroupBy(upgrade => upgrade.Xws, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var upgrade in upgrades.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(source, upgrade.Image.Replace('/', Path.DirectorySeparatorChar));
                RequireFile(sourcePath, $"{upgrade.Name} face");
                var slotId = FileId(upgrade.Slot);
                var backPath = Path.Combine(backRoot, $"{slotId}.png");
                RequireFile(backPath, $"{upgrade.Slot} card back");
                var canonicalId = xwsCounts[upgrade.Xws] > 1
                    ? Path.GetFileNameWithoutExtension(upgrade.Image)
                    : upgrade.Xws;
                var slotFolder = Path.Combine(destination, slotId);
                Directory.CreateDirectory(slotFolder);
                var facePath = Path.Combine(slotFolder, $"{canonicalId}.png");
                if (CopyIfChanged(sourcePath, facePath)) imported++; else unchanged++;

                var legacyCanonicalId = xwsCounts[upgrade.Xws] > 1 ? $"{upgrade.Xws}-{upgrade.Id}" : upgrade.Xws;
                var legacyCardFolder = Path.Combine(slotFolder, legacyCanonicalId);
                var legacyFacePath = Path.Combine(legacyCardFolder, "front.png");
                if (File.Exists(legacyFacePath))
                {
                    File.Delete(legacyFacePath);
                    legacyFilesRemoved++;
                }
                if (Directory.Exists(legacyCardFolder)
                    && !Directory.EnumerateFileSystemEntries(legacyCardFolder).Any())
                {
                    Directory.Delete(legacyCardFolder);
                    legacyFoldersRemoved++;
                }

                var conditionLinks = upgrade.Conditions.Select(conditionName =>
                {
                    var conditionXws = ConditionXws(conditionName);
                    var conditionFolder = Path.Combine(conditionRoot, conditionXws);
                    return new UpgradeConditionLink
                    {
                        ConditionName = conditionName,
                        ConditionXws = conditionXws,
                        FaceRepositoryPath = Relative(repository, Path.Combine(conditionFolder, "front.png")),
                        BackRepositoryPath = Relative(repository, Path.Combine(conditionFolder, "back.png")),
                        TokenRepositoryPath = Relative(repository, Path.Combine(repository, "assets", "source", "unified1e",
                            "condition-tokens", conditionXws is "mimicked" or "shadowed" ? "mimicked-shadowed.png" : $"{conditionXws}.png")),
                        AssetsAvailable = File.Exists(Path.Combine(conditionFolder, "front.png"))
                            && File.Exists(Path.Combine(conditionFolder, "back.png"))
                    };
                }).ToList();

                entries.Add(new UpgradeCardManifestEntry
                {
                    Id = upgrade.Id,
                    CanonicalId = canonicalId,
                    Name = upgrade.Name,
                    Xws = upgrade.Xws,
                    Slot = upgrade.Slot,
                    Points = upgrade.Points,
                    Unique = upgrade.Unique,
                    Limited = upgrade.Limited,
                    Faction = upgrade.Faction,
                    Text = upgrade.Text,
                    SourceRepositoryPath = Relative(repository, sourcePath),
                    FaceRepositoryPath = Relative(repository, facePath),
                    BackRepositoryPath = Relative(repository, backPath),
                    Face = DescribePng(facePath),
                    Back = DescribePng(backPath),
                    Conditions = conditionLinks
                });
            }

            var manifest = new UpgradeCardManifest
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceFolder = Relative(repository, source),
                DestinationFolder = Relative(repository, destination),
                UpgradeCardCount = entries.Count,
                ConditionLinkCount = entries.Sum(entry => entry.Conditions.Count),
                UpgradeCards = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Upgrade-Card Import");
            Console.WriteLine("=================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:          {repository}");
            Console.WriteLine($"Source:              {source}");
            Console.WriteLine($"Destination:         {destination}");
            Console.WriteLine($"Upgrade cards:       {entries.Count}");
            Console.WriteLine($"Upgrade types:       {entries.Select(entry => entry.Slot).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
            Console.WriteLine($"Condition links:     {manifest.ConditionLinkCount}");
            Console.WriteLine($"Imported or updated: {imported}");
            Console.WriteLine($"Unchanged:           {unchanged}");
            Console.WriteLine($"Old front.png files removed: {legacyFilesRemoved}");
            Console.WriteLine($"Empty card folders removed:  {legacyFoldersRemoved}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Upgrade faces were copied byte-for-byte into searchable flat filenames.");
            Console.WriteLine("Only obsolete generated front.png files and their empty per-card folders were removed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition upgrade-card import failed: {exception.Message}");
            return 1;
        }
    }

    private static List<UpgradeDefinition> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(item => new UpgradeDefinition
        {
            Id = Int(item, "id"), Name = Text(item, "name"), Xws = Text(item, "xws"),
            Slot = Text(item, "slot"), Points = Int(item, "points"), Text = Text(item, "text"),
            Image = Text(item, "image"), Faction = Text(item, "faction"),
            Unique = Bool(item, "unique"), Limited = Bool(item, "limited"),
            Conditions = Strings(item, "conditions")
        }).ToList();
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Int(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static bool Bool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static List<string> Strings(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(entry => entry.GetString() ?? "").Where(entry => entry.Length > 0).ToList()
            : new();
    private static string ConditionXws(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string FileId(string value) => value.ToLowerInvariant().Replace(' ', '_');
    private static bool CopyIfChanged(string source, string destination)
    {
        var bytes = File.ReadAllBytes(source);
        if (File.Exists(destination) && bytes.AsSpan().SequenceEqual(File.ReadAllBytes(destination))) return false;
        File.WriteAllBytes(destination, bytes); return true;
    }
    private static UpgradeCardPng DescribePng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature) || Encoding.ASCII.GetString(bytes, 12, 4) != "IHDR")
            throw new InvalidDataException($"Invalid PNG: {path}");
        return new UpgradeCardPng
        {
            Width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            Height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit import-first-edition-upgrade-cards <repository> [--source <folder>] [--destination <folder>] [--manifest <file>]");
    private sealed class UpgradeDefinition
    {
        public int Id { get; init; } public string Name { get; init; } = ""; public string Xws { get; init; } = "";
        public string Slot { get; init; } = ""; public int Points { get; init; } public string Text { get; init; } = "";
        public string Image { get; init; } = ""; public string Faction { get; init; } = ""; public bool Unique { get; init; }
        public bool Limited { get; init; } public List<string> Conditions { get; init; } = new();
    }
}

public sealed class UpgradeCardManifest
{
    public int SchemaVersion { get; init; } public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceFolder { get; init; } = ""; public string DestinationFolder { get; init; } = "";
    public int UpgradeCardCount { get; init; } public int ConditionLinkCount { get; init; }
    public List<UpgradeCardManifestEntry> UpgradeCards { get; init; } = new();
}
public sealed class UpgradeCardManifestEntry
{
    public int Id { get; init; } public string CanonicalId { get; init; } = ""; public string Name { get; init; } = ""; public string Xws { get; init; } = "";
    public string Slot { get; init; } = ""; public int Points { get; init; } public bool Unique { get; init; }
    public bool Limited { get; init; } public string Faction { get; init; } = ""; public string Text { get; init; } = "";
    public string SourceRepositoryPath { get; init; } = ""; public string FaceRepositoryPath { get; init; } = "";
    public string BackRepositoryPath { get; init; } = ""; public UpgradeCardPng Face { get; init; } = new();
    public UpgradeCardPng Back { get; init; } = new(); public List<UpgradeConditionLink> Conditions { get; init; } = new();
}
public sealed class UpgradeConditionLink
{
    public string ConditionName { get; init; } = ""; public string ConditionXws { get; init; } = "";
    public string FaceRepositoryPath { get; init; } = ""; public string BackRepositoryPath { get; init; } = "";
    public string TokenRepositoryPath { get; init; } = ""; public bool AssetsAvailable { get; init; }
}
public sealed class UpgradeCardPng { public int Width { get; init; } public int Height { get; init; } public string Sha256 { get; init; } = ""; }
