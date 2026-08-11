using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Imports the eight First Edition condition-token textures identified in the legacy runtime save.</summary>
public static class ImportFirstEditionConditionTokensCommand
{
    private static readonly ConditionTokenSource[] TokenSources =
    {
        Token("adebttopay", "A Debt to Pay", "asset__c9e9b25fc855a14f.png"),
        Token("fanaticaldevotion", "Fanatical Devotion", "asset__0ef5a15271cc4f21.png"),
        Token("harpooned", "Harpooned!", "asset__399275f5fcdcebeb.png"),
        Token("illshowyouthedarkside", "I'll Show You the Dark Side", "asset__36420c92f33a82c0.png"),
        Token("optimizedprototype", "Optimized Prototype", "asset__9b7f22b50c31355a.png"),
        Token("rattled", "Rattled", "asset__b250524ab77cdaf4.png"),
        Token("scrambled", "Scrambled", "asset__d0868e4f0015a93a.png"),
        Token("suppressivefire", "Suppressive Fire", "asset__834657a2f3841e6b.png",
            "Legacy runtime object is misspelled 'Suppresive Fire'; canonical spelling retained here.")
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
            var entries = new List<ConditionTokenManifestEntry>();
            foreach (var token in TokenSources.OrderBy(item => item.ConditionName, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(source, token.SourceFileName);
                RequireFile(sourcePath, $"{token.ConditionName} token texture");
                var destinationPath = Path.Combine(destination, $"{token.ConditionXws}.png");
                var bytes = File.ReadAllBytes(sourcePath);
                var (width, height) = ReadPngDimensions(bytes, sourcePath);
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

                entries.Add(new ConditionTokenManifestEntry
                {
                    ConditionXws = token.ConditionXws,
                    ConditionName = token.ConditionName,
                    SourceRepositoryPath = Relative(repository, sourcePath),
                    RepositoryPath = Relative(repository, destinationPath),
                    Width = width,
                    Height = height,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    Notes = token.Notes
                });
            }

            var manifest = new ConditionTokenManifest
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceFolder = Relative(repository, source),
                DestinationFolder = Relative(repository, destination),
                ConditionsWithoutTokens = new List<string> { "mimicked", "shadowed" },
                ConditionTokens = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Condition-Token Import");
            Console.WriteLine("===================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                {repository}");
            Console.WriteLine($"Source:                    {source}");
            Console.WriteLine($"Destination:               {destination}");
            Console.WriteLine($"Conditions requiring token: {entries.Count}");
            Console.WriteLine($"Imported or updated:       {imported}");
            Console.WriteLine($"Unchanged:                 {unchanged}");
            Console.WriteLine($"Conditions without tokens: {string.Join(", ", manifest.ConditionsWithoutTokens)}");
            Console.WriteLine($"Manifest:                  {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Condition-token textures imported successfully. PNG files were copied byte-for-byte.");
            Console.WriteLine("The unavailable shared legacy token mesh is intentionally not imported by this command.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition condition-token import failed: {exception.Message}");
            return 1;
        }
    }

    private static ConditionTokenSource Token(
        string xws, string name, string sourceFileName, string notes = "") =>
        new(xws, name, sourceFileName, notes);

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

    private sealed record ConditionTokenSource(
        string ConditionXws, string ConditionName, string SourceFileName, string Notes);
}

public sealed class ConditionTokenManifest
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceFolder { get; init; } = "";
    public string DestinationFolder { get; init; } = "";
    public List<string> ConditionsWithoutTokens { get; init; } = new();
    public List<ConditionTokenManifestEntry> ConditionTokens { get; init; } = new();
}

public sealed class ConditionTokenManifestEntry
{
    public string ConditionXws { get; init; } = "";
    public string ConditionName { get; init; } = "";
    public string SourceRepositoryPath { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = "";
    public string Notes { get; init; } = "";
}
