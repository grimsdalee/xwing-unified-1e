using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12A-2:
/// Registers the generated First Edition manoeuvre icons and builds the
/// authoritative runtime-code-to-icon lookup consumed by dial generation.
///
/// This command does not alter the source images. It writes:
///   - a generated asset manifest under assets/manifests;
///   - a runtime lookup contract under _unifiedtoolkit_reports; and
///   - validation reports proving every standard runtime manoeuvre resolves
///     to exactly one First Edition icon.
/// </summary>
public static class RegisterFirstEditionManeuverIconsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
            var iconLibraryPath = ResolveIconLibraryPath(repositoryRoot, args);
            var runtimeDataPath = ResolveRuntimeDataPath(repositoryRoot, args);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);

            ValidateFile(iconLibraryPath, "Phase 12A-1 icon-library manifest");
            ValidateFile(runtimeDataPath, "Phase 11F-1 runtime-data manifest");

            var library = Read<IconLibraryInput>(iconLibraryPath);
            var runtime = Read<ManeuverIconRegistrationRuntimeDataInput>(runtimeDataPath);

            var assets = BuildAssetRecords(repositoryRoot, library);
            var assetIndex = assets.ToDictionary(
                asset => BuildSemanticKey(asset.Difficulty, asset.Shape),
                asset => asset,
                StringComparer.OrdinalIgnoreCase);

            var lookups = new List<RuntimeManeuverIconLookup>();
            var missing = new List<string>();
            var ambiguous = new List<string>();

            foreach (var maneuver in runtime.Records
                         .Where(record => record.RuntimeType.Equals(
                             "Standard",
                             StringComparison.OrdinalIgnoreCase))
                         .SelectMany(record => record.Maneuvers)
                         .GroupBy(
                             item => item.RuntimeCode,
                             StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First())
                         .OrderBy(item => item.RuntimeCode, StringComparer.OrdinalIgnoreCase))
            {
                var semanticKey = BuildSemanticKey(
                    maneuver.Difficulty,
                    maneuver.Bearing);

                if (!assetIndex.TryGetValue(semanticKey, out var asset))
                {
                    missing.Add($"{maneuver.RuntimeCode} -> {semanticKey}");
                    continue;
                }

                lookups.Add(new RuntimeManeuverIconLookup
                {
                    RuntimeCode = maneuver.RuntimeCode,
                    SourceCode = maneuver.SourceCode,
                    Shape = maneuver.Bearing,
                    Difficulty = maneuver.Difficulty,
                    SemanticKey = semanticKey,
                    AssetId = asset.AssetId,
                    AssetPath = asset.RepositoryPath,
                    Width = asset.Width,
                    Height = asset.Height,
                    HasTransparency = asset.HasTransparency
                });
            }

            var duplicateRuntimeCodes = lookups
                .GroupBy(item => item.RuntimeCode, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            ambiguous.AddRange(duplicateRuntimeCodes);

            var manifestFolder = Path.Combine(
                repositoryRoot,
                "assets",
                "manifests");
            Directory.CreateDirectory(manifestFolder);
            Directory.CreateDirectory(outputFolder);

            var assetManifest = new FirstEditionManeuverIconAssetManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                AssetRole = "FirstEditionManeuverIcon",
                RepositoryRoot = NormalisePath(repositoryRoot),
                SourceManifest = NormalisePath(iconLibraryPath),
                AssetCount = assets.Count,
                Assets = assets
            };

            var contract = new FirstEditionManeuverIconRuntimeContract
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                IconLibraryPath = NormalisePath(iconLibraryPath),
                RuntimeDataPath = NormalisePath(runtimeDataPath),
                GeneratedIconAssets = assets.Count,
                RuntimeManeuverIds = lookups.Count + missing.Count,
                ResolvedRuntimeManeuvers = lookups.Count,
                AmbiguousMappings = ambiguous.Count,
                MissingMappings = missing.Count,
                Missing = missing,
                Ambiguous = ambiguous,
                Lookup = lookups
            };

            var assetManifestPath = Path.Combine(
                manifestFolder,
                "first-edition-maneuver-icons.json");
            var contractPath = Path.Combine(
                outputFolder,
                "first-edition-maneuver-icon-runtime-contract.json");
            var csvPath = Path.Combine(
                outputFolder,
                "first-edition-maneuver-icon-runtime-contract.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "FIRST-EDITION-MANEUVER-ICON-REGISTRATION.md");

            File.WriteAllText(
                assetManifestPath,
                JsonSerializer.Serialize(assetManifest, JsonOptions),
                new UTF8Encoding(false));
            File.WriteAllText(
                contractPath,
                JsonSerializer.Serialize(contract, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, lookups);
            WriteMarkdown(reportPath, contract, assets);

            Console.WriteLine(
                "UnifiedToolkit Phase 12A-2 First Edition Maneuver Icon Registration");
            Console.WriteLine(
                "====================================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                    {repositoryRoot}");
            Console.WriteLine($"Icon library:                  {iconLibraryPath}");
            Console.WriteLine($"Runtime data:                  {runtimeDataPath}");
            Console.WriteLine();
            Console.WriteLine($"Generated icon assets:         {assets.Count}");
            Console.WriteLine($"Runtime maneuver IDs:          {contract.RuntimeManeuverIds}");
            Console.WriteLine($"Resolved runtime maneuvers:    {contract.ResolvedRuntimeManeuvers}");
            Console.WriteLine($"Ambiguous mappings:            {contract.AmbiguousMappings}");
            Console.WriteLine($"Missing mappings:              {contract.MissingMappings}");
            Console.WriteLine($"Transparent PNG assets:        {assets.Count(asset => asset.HasTransparency)}");
            Console.WriteLine();
            Console.WriteLine($"Asset manifest:                {assetManifestPath}");
            Console.WriteLine($"Runtime contract:              {contractPath}");
            Console.WriteLine($"CSV:                           {csvPath}");
            Console.WriteLine($"Report:                        {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "First Edition maneuver icons registered. Source and generated images were not modified.");

            return contract.MissingMappings == 0
                && contract.AmbiguousMappings == 0
                && assets.Count == 26
                && contract.RuntimeManeuverIds == 63
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition maneuver-icon registration failed: {ex.Message}");
            return 1;
        }
    }

    private static List<FirstEditionManeuverIconAssetRecord> BuildAssetRecords(
        string repositoryRoot,
        IconLibraryInput library)
    {
        var result = new List<FirstEditionManeuverIconAssetRecord>();

        foreach (var entry in library.Entries
                     .OrderBy(item => item.Difficulty, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Shape, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(entry.TargetPath);
            ValidateFile(path, $"Generated icon '{entry.SemanticKey}'");

            var metadata = ReadPngMetadata(path);
            var repositoryPath = NormalisePath(
                Path.GetRelativePath(repositoryRoot, path));

            result.Add(new FirstEditionManeuverIconAssetRecord
            {
                AssetId = $"FE-MANEUVER-{StableId(entry.SemanticKey)}",
                AssetRole = "FirstEditionManeuverIcon",
                SemanticKey = entry.SemanticKey,
                Shape = entry.Shape,
                Difficulty = entry.Difficulty,
                RepositoryPath = repositoryPath,
                FileName = Path.GetFileName(path),
                Width = metadata.Width,
                Height = metadata.Height,
                PngColourType = metadata.ColourType,
                HasTransparency = metadata.HasTransparency,
                Sha256 = Sha256(path),
                SourceCodes = entry.SourceCodes
            });
        }

        var duplicateKeys = result
            .GroupBy(item => item.SemanticKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateKeys.Count > 0)
        {
            throw new InvalidDataException(
                $"Duplicate icon semantic keys: {string.Join(", ", duplicateKeys)}");
        }

        return result;
    }

    private static string BuildSemanticKey(string difficulty, string shape)
    {
        var semanticShape = shape switch
        {
            "TurnLeft" => "turn-left",
            "BankLeft" => "bank-left",
            "Straight" => "straight",
            "BankRight" => "bank-right",
            "TurnRight" => "turn-right",
            "KoiogranTurn" => "koiogran-turn",
            "Stop" => "stop",
            "ReverseBankLeft" => "reverse-bank-left",
            "ReverseStraight" => "reverse-straight",
            "ReverseBankRight" => "reverse-bank-right",
            "TallonRollLeft" => "tallon-roll-left",
            "TallonRollRight" => "tallon-roll-right",
            "SegnorsLoopLeft" => "segnors-loop-left",
            "SegnorsLoopRight" => "segnors-loop-right",
            _ => throw new InvalidDataException(
                $"Unsupported manoeuvre shape '{shape}'.")
        };

        return $"{difficulty.ToLowerInvariant()}:{semanticShape}";
    }

    private static PngMetadata ReadPngMetadata(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[26];

        if (stream.Read(header) != header.Length
            || header[0] != 0x89
            || header[1] != 0x50
            || header[2] != 0x4E
            || header[3] != 0x47)
        {
            throw new InvalidDataException($"Expected PNG image: {path}");
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

        var colourType = header[25];
        var hasTransparency = colourType is 4 or 6 || ContainsTrnsChunk(stream);

        return new PngMetadata(
            width,
            height,
            colourType,
            hasTransparency);
    }

    private static bool ContainsTrnsChunk(Stream stream)
    {
        stream.Position = 8;
        Span<byte> chunkHeader = stackalloc byte[8];

        while (stream.Position + 8 <= stream.Length)
        {
            if (stream.Read(chunkHeader) != 8)
                break;

            var length =
                (chunkHeader[0] << 24)
                | (chunkHeader[1] << 16)
                | (chunkHeader[2] << 8)
                | chunkHeader[3];

            var type = Encoding.ASCII.GetString(chunkHeader[4..8]);

            if (type == "tRNS")
                return true;

            if (type == "IEND")
                break;

            stream.Position += length + 4L;
        }

        return false;
    }

    private static string ResolveIconLibraryPath(
        string repositoryRoot,
        string[] args)
    {
        var option = ReadOption(args, "--icon-library");

        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12a",
                "maneuver-icon-library",
                "first-edition-maneuver-icon-library.json")
            : Path.GetFullPath(option);
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
                "maneuver-icon-registration")
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

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16];
    }

    private static void WriteCsv(
        string path,
        IEnumerable<RuntimeManeuverIconLookup> lookups)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "RuntimeCode,SourceCode,Shape,Difficulty,SemanticKey," +
            "AssetId,AssetPath,Width,Height,HasTransparency");

        foreach (var item in lookups)
        {
            writer.WriteLine(string.Join(',',
                Csv(item.RuntimeCode),
                Csv(item.SourceCode),
                Csv(item.Shape),
                Csv(item.Difficulty),
                Csv(item.SemanticKey),
                Csv(item.AssetId),
                Csv(item.AssetPath),
                item.Width,
                item.Height,
                item.HasTransparency));
        }
    }

    private static void WriteMarkdown(
        string path,
        FirstEditionManeuverIconRuntimeContract contract,
        IReadOnlyList<FirstEditionManeuverIconAssetRecord> assets)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12A-2 – First Edition Manoeuvre Icon Registration");
        writer.WriteLine();
        writer.WriteLine(
            $"- Generated icon assets: **{contract.GeneratedIconAssets}**");
        writer.WriteLine(
            $"- Runtime manoeuvre IDs: **{contract.RuntimeManeuverIds}**");
        writer.WriteLine(
            $"- Resolved runtime manoeuvres: **{contract.ResolvedRuntimeManeuvers}**");
        writer.WriteLine(
            $"- Ambiguous mappings: **{contract.AmbiguousMappings}**");
        writer.WriteLine(
            $"- Missing mappings: **{contract.MissingMappings}**");
        writer.WriteLine();
        writer.WriteLine("| Runtime code | Difficulty | Shape | Asset |");
        writer.WriteLine("|---|---|---|---|");

        foreach (var item in contract.Lookup)
        {
            writer.WriteLine(
                $"| `{Md(item.RuntimeCode)}` | {Md(item.Difficulty)} | " +
                $"{Md(item.Shape)} | `{Md(item.AssetPath)}` |");
        }

        writer.WriteLine();
        writer.WriteLine("## Asset validation");
        writer.WriteLine();
        writer.WriteLine(
            $"Transparent PNGs: **{assets.Count(asset => asset.HasTransparency)} / {assets.Count}**");
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

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  register-first-edition-maneuver-icons " +
            "<first-edition-repository> [--icon-library <file>] " +
            "[--runtime-data <file>] [--output <folder>]");
    }

    private sealed record PngMetadata(
        int Width,
        int Height,
        int ColourType,
        bool HasTransparency);
}

public sealed class FirstEditionManeuverIconAssetManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string AssetRole { get; init; } = string.Empty;
    public string RepositoryRoot { get; init; } = string.Empty;
    public string SourceManifest { get; init; } = string.Empty;
    public int AssetCount { get; init; }
    public List<FirstEditionManeuverIconAssetRecord> Assets { get; init; } = new();
}

public sealed class FirstEditionManeuverIconAssetRecord
{
    public string AssetId { get; init; } = string.Empty;
    public string AssetRole { get; init; } = string.Empty;
    public string SemanticKey { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int PngColourType { get; init; }
    public bool HasTransparency { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public List<string> SourceCodes { get; init; } = new();
}

public sealed class FirstEditionManeuverIconRuntimeContract
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string IconLibraryPath { get; init; } = string.Empty;
    public string RuntimeDataPath { get; init; } = string.Empty;
    public int GeneratedIconAssets { get; init; }
    public int RuntimeManeuverIds { get; init; }
    public int ResolvedRuntimeManeuvers { get; init; }
    public int AmbiguousMappings { get; init; }
    public int MissingMappings { get; init; }
    public List<string> Missing { get; init; } = new();
    public List<string> Ambiguous { get; init; } = new();
    public List<RuntimeManeuverIconLookup> Lookup { get; init; } = new();
}

public sealed class RuntimeManeuverIconLookup
{
    public string RuntimeCode { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string SemanticKey { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string AssetPath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public bool HasTransparency { get; init; }
}

public sealed class IconLibraryInput
{
    public List<IconLibraryEntryInput> Entries { get; init; } = new();
}

public sealed class IconLibraryEntryInput
{
    public string SemanticKey { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public List<string> SourceCodes { get; init; } = new();
}

public sealed class ManeuverIconRegistrationRuntimeDataInput
{
    public List<RuntimeDataRecordInput> Records { get; init; } = new();
}

public sealed class RuntimeDataRecordInput
{
    public string RuntimeType { get; init; } = string.Empty;
    public List<RuntimeManeuverInput> Maneuvers { get; init; } = new();
}

public sealed class RuntimeManeuverInput
{
    public string SourceCode { get; init; } = string.Empty;
    public string RuntimeCode { get; init; } = string.Empty;
    public string Bearing { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
}
