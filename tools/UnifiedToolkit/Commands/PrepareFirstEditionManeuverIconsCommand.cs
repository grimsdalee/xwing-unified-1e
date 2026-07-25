using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12A-0:
/// Identifies the blue 2.5 maneuver icons actually required by the 49 standard
/// First Edition ships and prepares byte-for-byte working copies for manual
/// recolouring to First Edition green.
///
/// The Unified source files are never modified.
/// </summary>
public static class PrepareFirstEditionManeuverIconsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, ManeuverIconDefinition> IconDefinitions =
        new Dictionary<string, ManeuverIconDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["tl"] = new("TurnLeft", "BlueTurnL", "teb.png"),
            ["bl"] = new("BankLeft", "BlueBankL", "beb.png"),
            ["s"] = new("Straight", "BlueStraight", "sb.png"),
            ["br"] = new("BankRight", "BlueBankR", "brb.png"),
            ["tr"] = new("TurnRight", "BlueTurnR", "trb.png"),
            ["k"] = new("KoiogranTurn", "BlueK", "kb.png"),
            ["stop"] = new("Stop", "BlueStall", "stopb.png"),
            ["blr"] = new("ReverseBankLeft", "BlueReverseBankL", "reb.png"),
            ["brr"] = new("ReverseBankRight", "BlueReverseBankR", "rrb.png"),
            ["sr"] = new("ReverseStraight", "BlueReverseStraight", "rsb.png"),
            ["tlt"] = new("TallonRollLeft", "BlueTalonL", "treb.png"),
            ["trt"] = new("TallonRollRight", "BlueTalonR", "trrb.png"),
            ["bls"] = new("SegnorsLoopLeft", "BlueSloopL", "s-loopeb.png"),
            ["brs"] = new("SegnorsLoopRight", "BlueSloopR", "s-looprb.png")
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
            var runtimePayloadPath = ResolveRuntimePayloadPath(repositoryRoot, args);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);
            var inventoryOnly = args.Any(value =>
                value.Equals("--inventory-only", StringComparison.OrdinalIgnoreCase));

            var sourceFolder = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified25",
                "assets",
                "dial",
                "maneuvers");

            ValidateFile(runtimePayloadPath, "Phase 11F-3 runtime payload manifest");
            ValidateDirectory(sourceFolder, "Unified maneuver icon folder");

            var runtime = Read<RuntimePayloadManifestInput>(runtimePayloadPath);
            var requiredKeys = runtime.Payloads
                .SelectMany(payload => payload.MoveSet)
                .Select(ParseIconKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var unsupportedKeys = requiredKeys
                .Where(key => !IconDefinitions.ContainsKey(key))
                .ToList();

            var workFolder = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "first-edition-maneuver-icons");

            var entries = new List<ManeuverIconPreparationEntry>();

            foreach (var key in requiredKeys.Where(IconDefinitions.ContainsKey))
            {
                var definition = IconDefinitions[key];
                var sourcePath = Path.Combine(sourceFolder, definition.FileName);
                var targetPath = Path.Combine(workFolder, definition.FileName);

                var status = "MissingSource";
                var sourceHash = string.Empty;
                var targetHash = string.Empty;

                if (File.Exists(sourcePath))
                {
                    sourceHash = Sha256(sourcePath);

                    if (inventoryOnly)
                    {
                        status = File.Exists(targetPath)
                            ? "TargetExists"
                            : "ReadyToCopy";
                    }
                    else
                    {
                        Directory.CreateDirectory(workFolder);

                        if (!File.Exists(targetPath))
                        {
                            File.Copy(sourcePath, targetPath, false);
                            status = "CopiedForRecolouring";
                        }
                        else
                        {
                            status = "TargetPreserved";
                        }

                        targetHash = Sha256(targetPath);
                    }
                }

                entries.Add(new ManeuverIconPreparationEntry
                {
                    RuntimeShapeKey = key,
                    Maneuver = definition.Maneuver,
                    UnifiedImageName = definition.UnifiedImageName,
                    FileName = definition.FileName,
                    SourcePath = NormalisePath(sourcePath),
                    TargetPath = NormalisePath(targetPath),
                    Status = status,
                    SourceSha256 = sourceHash,
                    TargetSha256 = targetHash,
                    ManualTask = File.Exists(targetPath)
                        ? "Recolour only the blue maneuver symbol to First Edition green. Preserve transparency, dimensions, antialiasing and every non-blue pixel."
                        : "Source image must be located before recolouring."
                });
            }

            Directory.CreateDirectory(outputFolder);

            var manifest = new ManeuverIconPreparationManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                RuntimePayloadPath = NormalisePath(runtimePayloadPath),
                UnifiedSourceFolder = NormalisePath(sourceFolder),
                FirstEditionWorkFolder = NormalisePath(workFolder),
                Mode = inventoryOnly ? "InventoryOnly" : "PrepareWorkingCopies",
                StandardShips = runtime.Payloads.Count,
                RequiredRuntimeShapes = requiredKeys.Count,
                PreparedIcons = entries.Count(entry =>
                    entry.Status is "CopiedForRecolouring" or "TargetPreserved" or "TargetExists"),
                MissingSourceIcons = entries.Count(entry => entry.Status == "MissingSource"),
                UnsupportedRuntimeShapes = unsupportedKeys,
                Entries = entries
            };

            var manifestPath = Path.Combine(
                outputFolder,
                "first-edition-maneuver-icon-preparation.json");
            var csvPath = Path.Combine(
                outputFolder,
                "first-edition-maneuver-icon-preparation.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "FIRST-EDITION-MANEUVER-ICON-PREPARATION.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, entries);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 12A-0 First Edition Maneuver Icon Preparation");
            Console.WriteLine("==================================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Runtime payloads:        {runtimePayloadPath}");
            Console.WriteLine($"Unified icon source:     {sourceFolder}");
            Console.WriteLine($"First Edition work area: {workFolder}");
            Console.WriteLine($"Mode:                    {manifest.Mode}");
            Console.WriteLine();
            Console.WriteLine($"Standard ships:          {manifest.StandardShips}");
            Console.WriteLine($"Required icon shapes:    {manifest.RequiredRuntimeShapes}");
            Console.WriteLine($"Prepared/present:        {manifest.PreparedIcons}");
            Console.WriteLine($"Missing source icons:    {manifest.MissingSourceIcons}");
            Console.WriteLine($"Unsupported shapes:      {manifest.UnsupportedRuntimeShapes.Count}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            Console.WriteLine();

            if (!inventoryOnly)
            {
                Console.WriteLine(
                    "Working copies are ready. Recolour the blue maneuver symbols to " +
                    "First Edition green in the work area; the Unified source files were not modified.");
            }
            else
            {
                Console.WriteLine(
                    "Inventory completed. No image files were copied or modified.");
            }

            return manifest.MissingSourceIcons == 0
                && manifest.UnsupportedRuntimeShapes.Count == 0
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition maneuver-icon preparation failed: {ex.Message}");
            return 1;
        }
    }

    private static string ParseIconKey(string runtimeCode)
    {
        var code = (runtimeCode ?? string.Empty).Trim().ToLowerInvariant();

        if (code.Length < 3)
            return code;

        var body = code[1..];

        var speedStart = body.IndexOfAny("0123456789".ToCharArray());
        if (speedStart < 0)
            return body;

        var bearing = body[..speedStart];
        var speedAndSuffix = body[speedStart..];
        var suffix = new string(speedAndSuffix.Where(char.IsLetter).ToArray());
        var speed = new string(speedAndSuffix.Where(char.IsDigit).ToArray());

        if (bearing == "s" && speed == "0")
            return "stop";

        return (bearing, suffix) switch
        {
            ("bl", "r") => "blr",
            ("br", "r") => "brr",
            ("s", "r") => "sr",
            ("tl", "t") => "tlt",
            ("tr", "t") => "trt",
            ("bl", "s") => "bls",
            ("br", "s") => "brs",
            _ => bearing
        };
    }

    private static RuntimePayloadManifestInput ReadRuntimePayloads(string path) =>
        Read<RuntimePayloadManifestInput>(path);

    private static T Read<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidDataException($"Could not parse JSON file: {path}");
    }

    private static string ResolveRuntimePayloadPath(
        string repositoryRoot,
        string[] args)
    {
        var option = ReadOption(args, "--runtime-payloads");

        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase11f",
                "standard-runtime-payloads",
                "standard-first-edition-runtime-payloads.json")
            : Path.GetFullPath(option);
    }

    private static string ResolveOutputFolder(
        string repositoryRoot,
        string[] args)
    {
        var option = ReadOption(args, "--output");

        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12a",
                "maneuver-icon-preparation")
            : Path.GetFullPath(option);
    }

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteCsv(
        string path,
        IEnumerable<ManeuverIconPreparationEntry> entries)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "RuntimeShapeKey,Maneuver,UnifiedImageName,FileName,Status," +
            "SourcePath,TargetPath,SourceSha256,TargetSha256,ManualTask");

        foreach (var entry in entries)
        {
            writer.WriteLine(string.Join(',',
                Csv(entry.RuntimeShapeKey),
                Csv(entry.Maneuver),
                Csv(entry.UnifiedImageName),
                Csv(entry.FileName),
                Csv(entry.Status),
                Csv(entry.SourcePath),
                Csv(entry.TargetPath),
                Csv(entry.SourceSha256),
                Csv(entry.TargetSha256),
                Csv(entry.ManualTask)));
        }
    }

    private static void WriteMarkdown(
        string path,
        ManeuverIconPreparationManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));

        writer.WriteLine("# Phase 12A-0 – First Edition Maneuver Icons");
        writer.WriteLine();
        writer.WriteLine($"Source: `{manifest.UnifiedSourceFolder}`  ");
        writer.WriteLine($"Working copies: `{manifest.FirstEditionWorkFolder}`");
        writer.WriteLine();
        writer.WriteLine(
            "Only the **blue maneuver symbol** should be recoloured. Preserve the " +
            "transparent background, dimensions, antialiasing and all other pixels.");
        writer.WriteLine();
        writer.WriteLine("| Maneuver | Logical image | File | Status |");
        writer.WriteLine("|---|---|---|---|");

        foreach (var entry in manifest.Entries)
        {
            writer.WriteLine(
                $"| {Md(entry.Maneuver)} | `{Md(entry.UnifiedImageName)}` | " +
                $"`{Md(entry.FileName)}` | {Md(entry.Status)} |");
        }

        if (manifest.UnsupportedRuntimeShapes.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Unsupported runtime shapes");
            foreach (var shape in manifest.UnsupportedRuntimeShapes)
                writer.WriteLine($"- `{Md(shape)}`");
        }
    }

    private static string NormalisePath(string path) =>
        path.Replace('\\', '/');

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string Md(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} was not found.", path);
    }

    private static void ValidateDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{description} was not found: {path}");
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  prepare-first-edition-maneuver-icons <first-edition-repository> " +
            "[--runtime-payloads <file>] [--output <folder>] [--inventory-only]");
    }

    private sealed record ManeuverIconDefinition(
        string Maneuver,
        string UnifiedImageName,
        string FileName);
}

public sealed class ManeuverIconPreparationManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string RuntimePayloadPath { get; init; } = string.Empty;
    public string UnifiedSourceFolder { get; init; } = string.Empty;
    public string FirstEditionWorkFolder { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int StandardShips { get; init; }
    public int RequiredRuntimeShapes { get; init; }
    public int PreparedIcons { get; init; }
    public int MissingSourceIcons { get; init; }
    public List<string> UnsupportedRuntimeShapes { get; init; } = new();
    public List<ManeuverIconPreparationEntry> Entries { get; init; } = new();
}

public sealed class ManeuverIconPreparationEntry
{
    public string RuntimeShapeKey { get; init; } = string.Empty;
    public string Maneuver { get; init; } = string.Empty;
    public string UnifiedImageName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public string TargetSha256 { get; init; } = string.Empty;
    public string ManualTask { get; init; } = string.Empty;
}

public sealed class RuntimePayloadManifestInput
{
    public List<RuntimePayloadInput> Payloads { get; init; } = new();
}

public sealed class RuntimePayloadInput
{
    public string ShipId { get; init; } = string.Empty;
    public List<string> MoveSet { get; init; } = new();
}
