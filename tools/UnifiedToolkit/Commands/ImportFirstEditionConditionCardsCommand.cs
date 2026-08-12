using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Imports the ten First Edition condition-card faces and their curated individual backs.</summary>
public static class ImportFirstEditionConditionCardsCommand
{
    private static readonly IReadOnlyDictionary<string, string> BackFileNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["adebttopay"] = "a-debt-to-pay-back.png",
            ["fanaticaldevotion"] = "fanatical-devotion-back.png",
            ["harpooned"] = "harpooned-back.png",
            ["illshowyouthedarkside"] = "ill-show-you-the-dark-side-back.png",
            ["mimicked"] = "mimicked-back.png",
            ["optimizedprototype"] = "optimizedprototype-back.png",
            ["rattled"] = "rattled-back.png",
            ["scrambled"] = "scrambled-back.png",
            ["shadowed"] = "shadowed-back.png",
            ["suppressivefire"] = "suppressive-fire-back.png"
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
            var faces = Path.GetFullPath(Option(args, "--faces") ?? Path.Combine(
                repository, "assets", "source", "xwing-data", "images", "conditions"));
            var backs = Path.GetFullPath(Option(args, "--backs") ?? Path.Combine(
                repository, "assets", "source", "xwing-data", "images", "condition-backs"));
            var destination = Path.GetFullPath(Option(args, "--destination") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "condition-cards"));
            var manifestPath = Path.GetFullPath(Option(args, "--manifest") ?? Path.Combine(
                repository, "assets", "source", "unified1e", "reference", "cards",
                "condition-cards.json"));
            var conditionDataPath = Path.Combine(repository, "source", "xwing-data", "data", "conditions.js");

            RequireDirectory(repository, "Repository");
            RequireDirectory(faces, "Condition-card face source");
            RequireDirectory(backs, "Condition-card back source");
            RequireFile(conditionDataPath, "xwing-data condition definitions");

            var conditions = LoadConditions(conditionDataPath);
            var unknown = conditions.Select(condition => condition.Xws)
                .Where(xws => !BackFileNames.ContainsKey(xws)).ToList();
            var unused = BackFileNames.Keys
                .Where(xws => conditions.All(condition => !condition.Xws.Equals(xws, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (conditions.Count != 10 || unknown.Count > 0 || unused.Count > 0)
            {
                throw new InvalidDataException(
                    $"Expected an exact ten-condition mapping. Definitions: {conditions.Count}; " +
                    $"unmapped: {string.Join(", ", unknown)}; unused mappings: {string.Join(", ", unused)}.");
            }

            Directory.CreateDirectory(destination);
            var imported = 0;
            var unchanged = 0;
            var entries = new List<ConditionCardManifestEntry>();

            foreach (var condition in conditions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var sourceFace = Path.Combine(faces, Path.GetFileName(condition.Image));
                var sourceBack = Path.Combine(backs, BackFileNames[condition.Xws]);
                RequireFile(sourceFace, $"{condition.Name} face");
                RequireFile(sourceBack, $"{condition.Name} back");

                var cardFolder = Path.Combine(destination, condition.Xws);
                Directory.CreateDirectory(cardFolder);
                var destinationFace = Path.Combine(cardFolder, "front.png");
                var destinationBack = Path.Combine(cardFolder, "back.png");
                if (CopyIfChanged(sourceFace, destinationFace)) imported++; else unchanged++;
                if (CopyIfChanged(sourceBack, destinationBack)) imported++; else unchanged++;

                var faceDescription = DescribePng(destinationFace);
                var backDescription = DescribePng(destinationBack);
                if (faceDescription.Width != backDescription.Width
                    || faceDescription.Height != backDescription.Height)
                {
                    throw new InvalidDataException(
                        $"{condition.Name} face/back dimensions do not match: " +
                        $"{faceDescription.Width}x{faceDescription.Height} and " +
                        $"{backDescription.Width}x{backDescription.Height}.");
                }

                entries.Add(new ConditionCardManifestEntry
                {
                    Name = condition.Name,
                    Xws = condition.Xws,
                    SourceFaceRepositoryPath = Relative(repository, sourceFace),
                    SourceBackRepositoryPath = Relative(repository, sourceBack),
                    FaceRepositoryPath = Relative(repository, destinationFace),
                    BackRepositoryPath = Relative(repository, destinationBack),
                    Face = faceDescription,
                    Back = backDescription
                });
            }

            var manifest = new ConditionCardManifest
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                FaceSourceFolder = Relative(repository, faces),
                BackSourceFolder = Relative(repository, backs),
                DestinationFolder = Relative(repository, destination),
                ConditionCardCount = entries.Count,
                ConditionCards = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Condition-Card Import");
            Console.WriteLine("===================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:            {repository}");
            Console.WriteLine($"Face source:           {faces}");
            Console.WriteLine($"Individual back source: {backs}");
            Console.WriteLine($"Destination:           {destination}");
            Console.WriteLine($"Condition cards:       {entries.Count}");
            Console.WriteLine($"Imported or updated:   {imported}");
            Console.WriteLine($"Unchanged files:       {unchanged}");
            Console.WriteLine($"Manifest:              {manifestPath}");
            Console.WriteLine();
            Console.WriteLine("Condition-card faces and their individual backs were imported successfully.");
            Console.WriteLine("PNG files were copied byte-for-byte; no artwork was generated or altered.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition condition-card import failed: {exception.Message}");
            return 1;
        }
    }

    private static List<ConditionDefinition> LoadConditions(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(item => new ConditionDefinition(
            item.GetProperty("name").GetString() ?? "",
            item.GetProperty("xws").GetString() ?? "",
            item.GetProperty("image").GetString() ?? "")).ToList();
    }

    private static bool CopyIfChanged(string source, string destination)
    {
        var sourceBytes = File.ReadAllBytes(source);
        if (File.Exists(destination) && sourceBytes.AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
            return false;
        File.WriteAllBytes(destination, sourceBytes);
        return true;
    }

    private static ConditionCardPng DescribePng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature)
            || Encoding.ASCII.GetString(bytes, 12, 4) != "IHDR")
            throw new InvalidDataException($"File is not a valid PNG with an IHDR header: {path}");
        return new ConditionCardPng
        {
            Width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            Height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private static string? Option(string[] args, string name) =>
        Enumerable.Range(0, Math.Max(0, args.Length - 1))
            .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(index => args[index + 1]).FirstOrDefault();

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
        "Usage: UnifiedToolkit import-first-edition-condition-cards <repository> " +
        "[--faces <folder>] [--backs <folder>] [--destination <folder>] [--manifest <file>]");

    private sealed record ConditionDefinition(string Name, string Xws, string Image);
}

public sealed class ConditionCardManifest
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public string FaceSourceFolder { get; init; } = "";
    public string BackSourceFolder { get; init; } = "";
    public string DestinationFolder { get; init; } = "";
    public int ConditionCardCount { get; init; }
    public List<ConditionCardManifestEntry> ConditionCards { get; init; } = new();
}

public sealed class ConditionCardManifestEntry
{
    public string Name { get; init; } = "";
    public string Xws { get; init; } = "";
    public string SourceFaceRepositoryPath { get; init; } = "";
    public string SourceBackRepositoryPath { get; init; } = "";
    public string FaceRepositoryPath { get; init; } = "";
    public string BackRepositoryPath { get; init; } = "";
    public ConditionCardPng Face { get; init; } = new();
    public ConditionCardPng Back { get; init; } = new();
}

public sealed class ConditionCardPng
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = "";
}
