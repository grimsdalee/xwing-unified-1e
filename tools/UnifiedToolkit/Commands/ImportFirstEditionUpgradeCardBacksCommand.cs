using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Imports the curated First Edition upgrade-card backs into the canonical unified1e source tree.</summary>
public static class ImportFirstEditionUpgradeCardBacksCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedBacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Astromech"] = "astromech.png",
            ["Bomb"] = "bomb.png",
            ["Cannon"] = "cannon.png",
            ["Cargo"] = "cargo.png",
            ["Crew"] = "crew.png",
            ["Elite"] = "elite.png",
            ["Hardpoint"] = "hardpoint.png",
            ["Illicit"] = "illicit.png",
            ["Missile"] = "missile.png",
            ["Modification"] = "modification.png",
            ["Salvaged Astromech"] = "salvaged_astromech.png",
            ["System"] = "system.png",
            ["Team"] = "team.png",
            ["Tech"] = "tech.png",
            ["Title"] = "title.png",
            ["Torpedo"] = "torpedo.png",
            ["Turret"] = "turret.png"
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
            var repository = Path.GetFullPath(args[0]);
            var source = Path.GetFullPath(Option(args, "--source") ?? Path.Combine(
                repository, "assets", "source", "legacy1e-non-pilot",
                "steamusercontent-a.akamaihd.net", "upgrade_cards_backs"));
            var destination = Path.GetFullPath(Option(args, "--destination") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "upgrade-card-backs"));
            var manifestPath = Path.GetFullPath(Option(args, "--manifest") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "reference", "cards",
                "upgrade-card-backs.json"));

            RequireDirectory(repository, "Repository");
            RequireDirectory(source, "Curated upgrade-card back source");
            ValidateSource(source);

            Directory.CreateDirectory(destination);
            var entries = new List<UpgradeCardBackManifestEntry>();
            var imported = 0;
            var unchanged = 0;

            foreach (var expected in ExpectedBacks.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(source, expected.Value);
                var destinationPath = Path.Combine(destination, expected.Value);
                var sourceBytes = File.ReadAllBytes(sourcePath);
                var changed = !File.Exists(destinationPath)
                    || !sourceBytes.AsSpan().SequenceEqual(File.ReadAllBytes(destinationPath));

                if (changed)
                {
                    File.WriteAllBytes(destinationPath, sourceBytes);
                    imported++;
                }
                else
                {
                    unchanged++;
                }

                var (width, height) = ReadPngDimensions(sourceBytes, sourcePath);
                entries.Add(new UpgradeCardBackManifestEntry
                {
                    UpgradeType = expected.Key,
                    FileName = expected.Value,
                    SourceRepositoryPath = Relative(repository, sourcePath),
                    RepositoryPath = Relative(repository, destinationPath),
                    Width = width,
                    Height = height,
                    Sha256 = Convert.ToHexString(SHA256.HashData(sourceBytes))
                });
            }

            var manifest = new UpgradeCardBackManifest
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceFolder = Relative(repository, source),
                DestinationFolder = Relative(repository, destination),
                UpgradeCardBacks = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Upgrade-Card Back Import");
            Console.WriteLine("=====================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:          {repository}");
            Console.WriteLine($"Source:              {source}");
            Console.WriteLine($"Destination:         {destination}");
            Console.WriteLine($"Upgrade-card types:  {entries.Count}");
            Console.WriteLine($"Imported or updated: {imported}");
            Console.WriteLine($"Unchanged:           {unchanged}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Upgrade-card backs imported successfully. PNG files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition upgrade-card back import failed: {exception.Message}");
            return 1;
        }
    }

    private static void ValidateSource(string source)
    {
        var actual = Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expected = ExpectedBacks.Values.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        var missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase).ToList();
        var unexpected = actual.Except(expected, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0 && unexpected.Count == 0) return;

        var problems = new List<string>();
        if (missing.Count > 0) problems.Add($"missing: {string.Join(", ", missing)}");
        if (unexpected.Count > 0) problems.Add($"unexpected: {string.Join(", ", unexpected)}");
        throw new InvalidDataException($"Curated source must contain exactly the 17 expected PNG files ({string.Join("; ", problems)}).");
    }

    private static (int Width, int Height) ReadPngDimensions(byte[] bytes, string path)
    {
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature)
            || Encoding.ASCII.GetString(bytes, 12, 4) != "IHDR")
        {
            throw new InvalidDataException($"File is not a valid PNG with an IHDR header: {path}");
        }

        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    private static string? Option(string[] args, string name) =>
        Enumerable.Range(0, Math.Max(0, args.Length - 1))
            .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(index => args[index + 1])
            .FirstOrDefault();

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}");
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void ShowUsage() => Console.WriteLine(
        "Usage: UnifiedToolkit import-first-edition-upgrade-card-backs <repository> " +
        "[--source <folder>] [--destination <folder>] [--manifest <file>]");
}

public sealed class UpgradeCardBackManifest
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceFolder { get; init; } = "";
    public string DestinationFolder { get; init; } = "";
    public List<UpgradeCardBackManifestEntry> UpgradeCardBacks { get; init; } = new();
}

public sealed class UpgradeCardBackManifestEntry
{
    public string UpgradeType { get; init; } = "";
    public string FileName { get; init; } = "";
    public string SourceRepositoryPath { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = "";
}
