using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Imports and catalogues the nine physical First Edition condition-token designs.</summary>
public static class ImportFirstEditionConditionTokensCommand
{
    private static readonly LegacyConditionTokenSource[] LegacyTokenSources =
    {
        LegacyToken("adebttopay", "A Debt to Pay", "asset__c9e9b25fc855a14f.png"),
        LegacyToken("fanaticaldevotion", "Fanatical Devotion", "asset__0ef5a15271cc4f21.png"),
        LegacyToken("illshowyouthedarkside", "I'll Show You the Dark Side", "asset__36420c92f33a82c0.png"),
        LegacyToken("optimizedprototype", "Optimized Prototype", "asset__9b7f22b50c31355a.png"),
        LegacyToken("rattled", "Rattled", "asset__b250524ab77cdaf4.png"),
        LegacyToken("scrambled", "Scrambled", "asset__d0868e4f0015a93a.png"),
        LegacyToken("suppressivefire", "Suppressive Fire", "asset__834657a2f3841e6b.png",
            "Legacy runtime object is misspelled 'Suppresive Fire'; canonical spelling retained here.")
    };

    private static readonly CuratedConditionTokenSource[] CuratedTokenSources =
    {
        CuratedToken("harpooned", "harpooned.png",
            new[] { "harpooned" }, new[] { "Harpooned!" },
            "Curated from the improved First Edition VASSAL condition-token artwork; the malformed legacy texture must not overwrite it."),
        CuratedToken("mimicked-shadowed", "mimicked-shadowed.png",
            new[] { "mimicked", "shadowed" }, new[] { "Mimicked", "Shadowed" },
            "One physical token design shared by the Mimicked and Shadowed conditions; recovered from the First Edition VASSAL module.")
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
                "steamusercontent-a.akamaihd.net", "images"));
            var destination = Path.GetFullPath(Option(args, "--destination") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "condition-tokens"));
            var manifestPath = Path.GetFullPath(Option(args, "--manifest") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "reference", "cards",
                "condition-tokens.json"));

            RequireDirectory(repository, "Repository");
            RequireDirectory(source, "Legacy condition-token texture source");
            Directory.CreateDirectory(destination);

            var imported = 0;
            var unchanged = 0;
            var curatedRetained = 0;
            var entries = new List<ConditionTokenManifestEntry>();

            foreach (var token in LegacyTokenSources.OrderBy(item => item.ConditionName, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(source, token.SourceFileName);
                RequireFile(sourcePath, $"{token.ConditionName} token texture");
                var destinationPath = Path.Combine(destination, $"{token.ConditionXws}.png");
                var bytes = File.ReadAllBytes(sourcePath);
                var changed = !File.Exists(destinationPath)
                    || !bytes.AsSpan().SequenceEqual(File.ReadAllBytes(destinationPath));

                if (changed)
                {
                    File.WriteAllBytes(destinationPath, bytes);
                    imported++;
                }
                else
                {
                    unchanged++;
                }

                entries.Add(ManifestEntry(
                    token.ConditionXws,
                    new[] { token.ConditionXws },
                    new[] { token.ConditionName },
                    sourcePath,
                    destinationPath,
                    bytes,
                    repository,
                    token.Notes));
            }

            foreach (var token in CuratedTokenSources.OrderBy(item => item.TokenId, StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(destination, token.FileName);
                RequireFile(path, $"{string.Join("/", token.ConditionNames)} curated token texture");
                var bytes = File.ReadAllBytes(path);
                entries.Add(ManifestEntry(
                    token.TokenId,
                    token.ConditionXws,
                    token.ConditionNames,
                    path,
                    path,
                    bytes,
                    repository,
                    token.Notes));
                curatedRetained++;
            }

            var conditionAssignments = entries.Sum(entry => entry.ConditionXws.Count);
            var manifest = new ConditionTokenManifest
            {
                SchemaVersion = 2,
                GeneratedUtc = DateTimeOffset.UtcNow,
                LegacySourceFolder = Relative(repository, source),
                DestinationFolder = Relative(repository, destination),
                PhysicalTokenDesignCount = entries.Count,
                ConditionAssignmentCount = conditionAssignments,
                ConditionTokens = entries.OrderBy(entry => entry.TokenId, StringComparer.OrdinalIgnoreCase).ToList()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Condition-Token Import");
            Console.WriteLine("===================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                 {repository}");
            Console.WriteLine($"Legacy source:              {source}");
            Console.WriteLine($"Destination:                {destination}");
            Console.WriteLine($"Condition cards assigned:   {conditionAssignments}");
            Console.WriteLine($"Physical token designs:     {entries.Count}");
            Console.WriteLine($"Imported or updated:        {imported}");
            Console.WriteLine($"Unchanged legacy imports:   {unchanged}");
            Console.WriteLine($"Curated textures preserved: {curatedRetained}");
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Condition-token artwork imported and catalogued successfully.");
            Console.WriteLine("The curated Harpooned and Mimicked/Shadowed textures were validated but not overwritten.");
            Console.WriteLine("The unavailable shared legacy token mesh is intentionally not imported by this command.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition condition-token import failed: {exception.Message}");
            return 1;
        }
    }

    private static ConditionTokenManifestEntry ManifestEntry(
        string tokenId,
        IEnumerable<string> conditionXws,
        IEnumerable<string> conditionNames,
        string sourcePath,
        string destinationPath,
        byte[] bytes,
        string repository,
        string notes)
    {
        var (width, height) = ReadPngDimensions(bytes, destinationPath);
        return new ConditionTokenManifestEntry
        {
            TokenId = tokenId,
            ConditionXws = conditionXws.ToList(),
            ConditionNames = conditionNames.ToList(),
            SourceRepositoryPath = Relative(repository, sourcePath),
            RepositoryPath = Relative(repository, destinationPath),
            Width = width,
            Height = height,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Notes = notes
        };
    }

    private static LegacyConditionTokenSource LegacyToken(
        string xws, string name, string sourceFileName, string notes = "") =>
        new(xws, name, sourceFileName, notes);

    private static CuratedConditionTokenSource CuratedToken(
        string tokenId, string fileName, string[] xws, string[] names, string notes) =>
        new(tokenId, fileName, xws, names, notes);

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

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path);
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void ShowUsage() => Console.WriteLine(
        "Usage: UnifiedToolkit import-first-edition-condition-tokens <repository> " +
        "[--source <folder>] [--destination <folder>] [--manifest <file>]");

    private sealed record LegacyConditionTokenSource(
        string ConditionXws, string ConditionName, string SourceFileName, string Notes);

    private sealed record CuratedConditionTokenSource(
        string TokenId, string FileName, string[] ConditionXws, string[] ConditionNames, string Notes);
}

public sealed class ConditionTokenManifest
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public string LegacySourceFolder { get; init; } = "";
    public string DestinationFolder { get; init; } = "";
    public int PhysicalTokenDesignCount { get; init; }
    public int ConditionAssignmentCount { get; init; }
    public List<ConditionTokenManifestEntry> ConditionTokens { get; init; } = new();
}

public sealed class ConditionTokenManifestEntry
{
    public string TokenId { get; init; } = "";
    public List<string> ConditionXws { get; init; } = new();
    public List<string> ConditionNames { get; init; } = new();
    public string SourceRepositoryPath { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = "";
    public string Notes { get; init; } = "";
}
