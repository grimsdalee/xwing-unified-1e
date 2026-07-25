using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12A-1:
/// Builds the semantic First Edition manoeuvre-icon library from:
///
///   Green: manually recoloured working copies under
///          assets/source/first-edition-maneuver-icons
///
///   White/Red: the matching Unified source icons under
///              assets/source/unified25/assets/dial/maneuvers
///
/// Only shape+difficulty combinations actually used by the 49 standard
/// First Edition ship dials are emitted. Unused curated icons (for example a
/// green Stop if no First Edition standard ship has one) are reported but are
/// not registered in the generated library.
/// </summary>
public static class BuildFirstEditionManeuverIconLibraryCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, IconShapeDefinition> Shapes =
        new Dictionary<string, IconShapeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["TurnLeft"] = new("turn-left", "teb.png"),
            ["BankLeft"] = new("bank-left", "beb.png"),
            ["Straight"] = new("straight", "sb.png"),
            ["BankRight"] = new("bank-right", "brb.png"),
            ["TurnRight"] = new("turn-right", "trb.png"),
            ["KoiogranTurn"] = new("koiogran-turn", "kb.png"),
            ["Stop"] = new("stop", "stopb.png"),
            ["ReverseBankLeft"] = new("reverse-bank-left", "reb.png"),
            ["ReverseStraight"] = new("reverse-straight", "rsb.png"),
            ["ReverseBankRight"] = new("reverse-bank-right", "rrb.png"),
            ["TallonRollLeft"] = new("tallon-roll-left", "treb.png"),
            ["TallonRollRight"] = new("tallon-roll-right", "trrb.png"),
            ["SegnorsLoopLeft"] = new("segnors-loop-left", "s-loopeb.png"),
            ["SegnorsLoopRight"] = new("segnors-loop-right", "s-looprb.png")
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
            var runtimeDataPath = ResolveRuntimeDataPath(repositoryRoot, args);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);
            var validateOnly = args.Any(value =>
                value.Equals("--validate-only", StringComparison.OrdinalIgnoreCase));

            var greenSourceFolder = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "first-edition-maneuver-icons");

            var unifiedSourceFolder = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified25",
                "assets",
                "dial",
                "maneuvers");

            var generatedRoot = Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "FirstEditionManeuverIcon");

            ValidateFile(runtimeDataPath, "Phase 11F-1 runtime-data manifest");
            ValidateDirectory(greenSourceFolder, "Curated First Edition green-icon folder");
            ValidateDirectory(unifiedSourceFolder, "Unified manoeuvre-icon source folder");

            var runtime = Read<RuntimeDataManifestInput>(runtimeDataPath);
            var required = runtime.Records
                .Where(record =>
                    record.RuntimeType.Equals("Standard", StringComparison.OrdinalIgnoreCase))
                .SelectMany(record => record.Maneuvers)
                .Select(maneuver => new RequiredIconCombination
                {
                    Shape = maneuver.Bearing,
                    Difficulty = maneuver.Difficulty,
                    SourceCode = maneuver.SourceCode
                })
                .GroupBy(
                    item => $"{item.Shape}|{item.Difficulty}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new RequiredIconCombination
                {
                    Shape = group.First().Shape,
                    Difficulty = group.First().Difficulty,
                    SourceCodes = group
                        .Select(item => item.SourceCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .OrderBy(item => item.Difficulty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Shape, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var entries = new List<FirstEditionManeuverIconEntry>();
            var issues = new List<string>();

            foreach (var requirement in required)
            {
                if (!Shapes.TryGetValue(requirement.Shape, out var shape))
                {
                    issues.Add(
                        $"No icon-shape mapping exists for '{requirement.Shape}' " +
                        $"({requirement.Difficulty}).");
                    continue;
                }

                var sourcePath = ResolveSourcePath(
                    requirement.Difficulty,
                    shape.BlueTemplateFileName,
                    greenSourceFolder,
                    unifiedSourceFolder);

                var generatedFileName = $"{shape.SemanticName}.png";
                var targetPath = Path.Combine(
                    generatedRoot,
                    requirement.Difficulty.ToLowerInvariant(),
                    generatedFileName);

                var entry = new FirstEditionManeuverIconEntry
                {
                    Shape = requirement.Shape,
                    Difficulty = requirement.Difficulty,
                    SemanticKey =
                        $"{requirement.Difficulty.ToLowerInvariant()}:{shape.SemanticName}",
                    SourceCodes = requirement.SourceCodes,
                    SourcePath = NormalisePath(sourcePath),
                    TargetPath = NormalisePath(targetPath),
                    GeneratedFileName = generatedFileName
                };

                if (!File.Exists(sourcePath))
                {
                    entry = entry with
                    {
                        Status = "MissingSource"
                    };
                    issues.Add(
                        $"Required {requirement.Difficulty} {requirement.Shape} icon " +
                        $"was not found: {sourcePath}");
                    entries.Add(entry);
                    continue;
                }

                var sourceHash = Sha256(sourcePath);
                var sourceDimensions = ReadPngDimensions(sourcePath);

                if (!validateOnly)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                    if (!File.Exists(targetPath)
                        || !Sha256(targetPath).Equals(
                            sourceHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(sourcePath, targetPath, true);
                        entry = entry with { Status = "Generated" };
                    }
                    else
                    {
                        entry = entry with { Status = "Unchanged" };
                    }
                }
                else
                {
                    entry = entry with
                    {
                        Status = File.Exists(targetPath)
                            ? "Validated"
                            : "ReadyToGenerate"
                    };
                }

                entry = entry with
                {
                    SourceSha256 = sourceHash,
                    Width = sourceDimensions.Width,
                    Height = sourceDimensions.Height,
                    TargetSha256 = File.Exists(targetPath)
                        ? Sha256(targetPath)
                        : string.Empty
                };

                entries.Add(entry);
            }

            var curatedFiles = Directory
                .EnumerateFiles(greenSourceFolder, "*.png", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(value => value is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var requiredGreenFiles = entries
                .Where(entry =>
                    entry.Difficulty.Equals("Green", StringComparison.OrdinalIgnoreCase))
                .Select(entry => Path.GetFileName(entry.SourcePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unusedGreenFiles = curatedFiles
                .Where(file => !requiredGreenFiles.Contains(file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Directory.CreateDirectory(outputFolder);

            var manifest = new FirstEditionManeuverIconLibraryManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                RuntimeDataPath = NormalisePath(runtimeDataPath),
                GreenSourceFolder = NormalisePath(greenSourceFolder),
                UnifiedSourceFolder = NormalisePath(unifiedSourceFolder),
                GeneratedRoot = NormalisePath(generatedRoot),
                Mode = validateOnly ? "ValidateOnly" : "Generate",
                StandardShips = runtime.Records.Count(record =>
                    record.RuntimeType.Equals("Standard", StringComparison.OrdinalIgnoreCase)),
                RequiredCombinations = required.Count,
                GeneratedOrReady = entries.Count(entry =>
                    entry.Status is "Generated" or "Unchanged" or "Validated" or "ReadyToGenerate"),
                MissingSources = entries.Count(entry => entry.Status == "MissingSource"),
                GreenCombinations = entries.Count(entry =>
                    entry.Difficulty.Equals("Green", StringComparison.OrdinalIgnoreCase)),
                WhiteCombinations = entries.Count(entry =>
                    entry.Difficulty.Equals("White", StringComparison.OrdinalIgnoreCase)),
                RedCombinations = entries.Count(entry =>
                    entry.Difficulty.Equals("Red", StringComparison.OrdinalIgnoreCase)),
                UnusedCuratedGreenFiles = unusedGreenFiles,
                Issues = issues,
                Entries = entries
            };

            var manifestPath = Path.Combine(
                outputFolder,
                "first-edition-maneuver-icon-library.json");
            var csvPath = Path.Combine(
                outputFolder,
                "first-edition-maneuver-icon-library.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "FIRST-EDITION-MANEUVER-ICON-LIBRARY.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, entries);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 12A-1 First Edition Maneuver Icon Library");
            Console.WriteLine(
                "================================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Runtime data:            {runtimeDataPath}");
            Console.WriteLine($"Green source:            {greenSourceFolder}");
            Console.WriteLine($"White/red source:        {unifiedSourceFolder}");
            Console.WriteLine($"Generated library:       {generatedRoot}");
            Console.WriteLine($"Mode:                    {manifest.Mode}");
            Console.WriteLine();
            Console.WriteLine($"Standard ships:          {manifest.StandardShips}");
            Console.WriteLine($"Required combinations:   {manifest.RequiredCombinations}");
            Console.WriteLine($"Green combinations:      {manifest.GreenCombinations}");
            Console.WriteLine($"White combinations:      {manifest.WhiteCombinations}");
            Console.WriteLine($"Red combinations:        {manifest.RedCombinations}");
            Console.WriteLine($"Generated/ready:         {manifest.GeneratedOrReady}");
            Console.WriteLine($"Missing source icons:    {manifest.MissingSources}");
            Console.WriteLine($"Unused curated greens:   {manifest.UnusedCuratedGreenFiles.Count}");
            Console.WriteLine($"Issues:                  {manifest.Issues.Count}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            Console.WriteLine();

            if (unusedGreenFiles.Count > 0)
            {
                Console.WriteLine(
                    "Curated green files not used by any standard First Edition dial:");
                foreach (var file in unusedGreenFiles)
                    Console.WriteLine($"  - {file}");
                Console.WriteLine();
            }

            Console.WriteLine(
                validateOnly
                    ? "Library validation completed. No generated assets were changed."
                    : "First Edition maneuver-icon library generated. Source files were not modified.");

            return manifest.MissingSources == 0 && manifest.Issues.Count == 0
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition maneuver-icon library failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveSourcePath(
        string difficulty,
        string blueTemplateFileName,
        string greenSourceFolder,
        string unifiedSourceFolder)
    {
        if (difficulty.Equals("Green", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(greenSourceFolder, blueTemplateFileName);
        }

        var suffix = difficulty.Equals("White", StringComparison.OrdinalIgnoreCase)
            ? 'w'
            : difficulty.Equals("Red", StringComparison.OrdinalIgnoreCase)
                ? 'r'
                : throw new InvalidDataException(
                    $"Unsupported First Edition manoeuvre difficulty '{difficulty}'.");

        var derivedFileName = ReplaceColourSuffix(
            blueTemplateFileName,
            suffix);

        return Path.Combine(unifiedSourceFolder, derivedFileName);
    }

    private static string ReplaceColourSuffix(
        string blueFileName,
        char replacement)
    {
        var extension = Path.GetExtension(blueFileName);
        var stem = Path.GetFileNameWithoutExtension(blueFileName);

        if (!stem.EndsWith('b'))
        {
            throw new InvalidDataException(
                $"Cannot derive colour variant from '{blueFileName}': " +
                "the blue template filename does not end in 'b'.");
        }

        return $"{stem[..^1]}{replacement}{extension}";
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];

        if (stream.Read(header) != header.Length
            || header[0] != 0x89
            || header[1] != 0x50
            || header[2] != 0x4E
            || header[3] != 0x47)
        {
            throw new InvalidDataException(
                $"Expected PNG image: {path}");
        }

        var width =
            (header[16] << 24)
            | (header[17] << 16)
            | (header[18] << 8)
            | header[19];

        var height =
            (header[20] << 24)
            | (header[21] << 16)
            | (header[22] << 8)
            | header[23];

        return (width, height);
    }

    private static T Read<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidDataException(
                   $"Could not parse JSON file: {path}");
    }

    private static string ResolveRuntimeDataPath(
        string repositoryRoot,
        string[] args)
    {
        var option = ReadOption(args, "--runtime-data");

        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase11f",
                "standard-runtime-data",
                "standard-first-edition-runtime-data.json")
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
                "maneuver-icon-library")
            : Path.GetFullPath(option);
    }

    private static string? ReadOption(
        string[] args,
        string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(
                    option,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
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
        IEnumerable<FirstEditionManeuverIconEntry> entries)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "SemanticKey,Shape,Difficulty,SourceCodes,Status,Width,Height," +
            "SourcePath,TargetPath,SourceSha256,TargetSha256");

        foreach (var entry in entries)
        {
            writer.WriteLine(string.Join(',',
                Csv(entry.SemanticKey),
                Csv(entry.Shape),
                Csv(entry.Difficulty),
                Csv(string.Join('|', entry.SourceCodes)),
                Csv(entry.Status),
                entry.Width,
                entry.Height,
                Csv(entry.SourcePath),
                Csv(entry.TargetPath),
                Csv(entry.SourceSha256),
                Csv(entry.TargetSha256)));
        }
    }

    private static void WriteMarkdown(
        string path,
        FirstEditionManeuverIconLibraryManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12A-1 – First Edition Manoeuvre Icon Library");
        writer.WriteLine();
        writer.WriteLine(
            $"Required combinations: **{manifest.RequiredCombinations}**  ");
        writer.WriteLine(
            $"Green: **{manifest.GreenCombinations}**, " +
            $"White: **{manifest.WhiteCombinations}**, " +
            $"Red: **{manifest.RedCombinations}**  ");
        writer.WriteLine(
            $"Missing sources: **{manifest.MissingSources}**");
        writer.WriteLine();
        writer.WriteLine(
            "Only shape+difficulty combinations present on standard First Edition " +
            "ship dials are included. An unused curated green icon is not evidence " +
            "that the manoeuvre existed as green in First Edition.");
        writer.WriteLine();
        writer.WriteLine("| Difficulty | Shape | Semantic key | Source codes | Status |");
        writer.WriteLine("|---|---|---|---|---|");

        foreach (var entry in manifest.Entries)
        {
            writer.WriteLine(
                $"| {Md(entry.Difficulty)} | {Md(entry.Shape)} | " +
                $"`{Md(entry.SemanticKey)}` | " +
                $"{Md(string.Join(", ", entry.SourceCodes))} | " +
                $"{Md(entry.Status)} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Unused curated green files");
        writer.WriteLine();

        if (manifest.UnusedCuratedGreenFiles.Count == 0)
        {
            writer.WriteLine("None.");
        }
        else
        {
            foreach (var file in manifest.UnusedCuratedGreenFiles)
                writer.WriteLine($"- `{Md(file)}`");
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

    private static void ValidateFile(
        string path,
        string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{description} was not found.",
                path);
        }
    }

    private static void ValidateDirectory(
        string path,
        string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"{description} was not found: {path}");
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  build-first-edition-maneuver-icon-library " +
            "<first-edition-repository> [--runtime-data <file>] " +
            "[--output <folder>] [--validate-only]");
    }

    private sealed record IconShapeDefinition(
        string SemanticName,
        string BlueTemplateFileName);
}

public sealed class FirstEditionManeuverIconLibraryManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string RuntimeDataPath { get; init; } = string.Empty;
    public string GreenSourceFolder { get; init; } = string.Empty;
    public string UnifiedSourceFolder { get; init; } = string.Empty;
    public string GeneratedRoot { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int StandardShips { get; init; }
    public int RequiredCombinations { get; init; }
    public int GeneratedOrReady { get; init; }
    public int MissingSources { get; init; }
    public int GreenCombinations { get; init; }
    public int WhiteCombinations { get; init; }
    public int RedCombinations { get; init; }
    public List<string> UnusedCuratedGreenFiles { get; init; } = new();
    public List<string> Issues { get; init; } = new();
    public List<FirstEditionManeuverIconEntry> Entries { get; init; } = new();
}

public sealed record FirstEditionManeuverIconEntry
{
    public string SemanticKey { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public List<string> SourceCodes { get; init; } = new();
    public string GeneratedFileName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public string SourceSha256 { get; init; } = string.Empty;
    public string TargetSha256 { get; init; } = string.Empty;
}

public sealed class RequiredIconCombination
{
    public string Shape { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public List<string> SourceCodes { get; init; } = new();
}

public sealed class RuntimeDataManifestInput
{
    public List<RuntimeDataShipInput> Records { get; init; } = new();
}

public sealed class RuntimeDataShipInput
{
    public string RuntimeType { get; init; } = string.Empty;
    public List<RuntimeDataManeuverInput> Maneuvers { get; init; } = new();
}

public sealed class RuntimeDataManeuverInput
{
    public string SourceCode { get; init; } = string.Empty;
    public string Bearing { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
}
