using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12A-3:
/// Produces a First Edition replacement of the existing Unified dial runtime.
///
/// The generated runtime:
///   - preserves the proven Unified movement implementation;
///   - interprets runtime prefix 'b' as First Edition Green;
///   - removes Purple from the manoeuvre editor colour cycle;
///   - installs the 26 registered semantic manoeuvre icons;
///   - retains the original speed-number images; and
///   - never modifies assets/source/unified25.
/// </summary>
public static class BuildFirstEditionDialRuntimeCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, string> LogicalSuffixByShape =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TurnLeft"] = "TurnL",
            ["BankLeft"] = "BankL",
            ["Straight"] = "Straight",
            ["BankRight"] = "BankR",
            ["TurnRight"] = "TurnR",
            ["KoiogranTurn"] = "K",
            ["Stop"] = "Stall",
            ["ReverseBankLeft"] = "ReverseBankL",
            ["ReverseStraight"] = "ReverseStraight",
            ["ReverseBankRight"] = "ReverseBankR",
            ["TallonRollLeft"] = "TalonL",
            ["TallonRollRight"] = "TalonR",
            ["SegnorsLoopLeft"] = "SloopL",
            ["SegnorsLoopRight"] = "SloopR"
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
            var contractPath = ResolveContractPath(repositoryRoot, args);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);
            var assetBaseUrl = ResolveAssetBaseUrl(args);

            var sourceDialFolder = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified25",
                "TTS_xwing",
                "src",
                "Dial");

            var generatedRoot = Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "FirstEditionDialRuntime");

            var generatedDialFolder = Path.Combine(generatedRoot, "Dial");

            ValidateFile(contractPath, "Phase 12A-2 manoeuvre-icon contract");
            ValidateDirectory(sourceDialFolder, "Unified Dial source folder");

            var contract = Read<IconRuntimeContractInput>(contractPath);

            if (contract.MissingMappings != 0 || contract.AmbiguousMappings != 0)
            {
                throw new InvalidDataException(
                    "The manoeuvre-icon contract is not fully resolved.");
            }

            if (contract.Lookup.Count != 63)
            {
                throw new InvalidDataException(
                    $"Expected 63 runtime manoeuvre mappings, found {contract.Lookup.Count}.");
            }

            if (Directory.Exists(generatedDialFolder))
                Directory.Delete(generatedDialFolder, true);

            CopyDirectory(sourceDialFolder, generatedDialFolder);

            var editorLuaPath = Path.Combine(
                generatedDialFolder,
                "ManeuverSetEditor.lua");
            var unassignedXmlPath = Path.Combine(
                generatedDialFolder,
                "UnassignedDial.xml");

            ValidateFile(editorLuaPath, "Generated ManeuverSetEditor.lua");
            ValidateFile(unassignedXmlPath, "Generated UnassignedDial.xml");

            var logicalAssets = BuildLogicalAssets(
                repositoryRoot,
                contract.Lookup,
                assetBaseUrl);

            PatchManeuverSetEditor(editorLuaPath, logicalAssets);
            PatchUnassignedDialXml(unassignedXmlPath);

            var generatedFiles = Directory
                .EnumerateFiles(generatedDialFolder, "*", SearchOption.AllDirectories)
                .Select(path => NormalisePath(
                    Path.GetRelativePath(repositoryRoot, path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var validation = ValidateGeneratedRuntime(
                editorLuaPath,
                unassignedXmlPath,
                logicalAssets);

            Directory.CreateDirectory(outputFolder);

            var manifest = new FirstEditionDialRuntimeManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                SourceDialFolder = NormalisePath(sourceDialFolder),
                GeneratedRoot = NormalisePath(generatedRoot),
                IconContractPath = NormalisePath(contractPath),
                AssetBaseUrl = assetBaseUrl,
                RuntimeManeuverMappings = contract.Lookup.Count,
                LogicalIconAssets = logicalAssets.Count,
                GeneratedFiles = generatedFiles,
                ValidationErrors = validation.Errors,
                ValidationWarnings = validation.Warnings,
                Assets = logicalAssets
            };

            var manifestPath = Path.Combine(
                outputFolder,
                "first-edition-dial-runtime.json");
            var csvPath = Path.Combine(
                outputFolder,
                "first-edition-dial-runtime-assets.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "FIRST-EDITION-DIAL-RUNTIME-REPORT.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, logicalAssets);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 12A-3 First Edition Dial Runtime Integration");
            Console.WriteLine(
                "==================================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                 {repositoryRoot}");
            Console.WriteLine($"Unified Dial source:        {sourceDialFolder}");
            Console.WriteLine($"Icon contract:              {contractPath}");
            Console.WriteLine($"Asset base URL:             {assetBaseUrl}");
            Console.WriteLine($"Generated runtime:          {generatedRoot}");
            Console.WriteLine();
            Console.WriteLine($"Runtime manoeuvre mappings: {contract.Lookup.Count}");
            Console.WriteLine($"Logical manoeuvre assets:   {logicalAssets.Count}");
            Console.WriteLine($"Generated Dial files:       {generatedFiles.Count}");
            Console.WriteLine($"Validation errors:          {validation.Errors.Count}");
            Console.WriteLine($"Validation warnings:        {validation.Warnings.Count}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine($"Asset CSV:                  {csvPath}");
            Console.WriteLine($"Report:                     {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "First Edition dial runtime generated. Unified source files were not modified.");

            return validation.Errors.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition dial-runtime generation failed: {ex.Message}");
            return 1;
        }
    }

    private static List<FirstEditionDialLogicalAsset> BuildLogicalAssets(
        string repositoryRoot,
        IReadOnlyList<IconLookupInput> lookup,
        string assetBaseUrl)
    {
        var result = lookup
            .GroupBy(
                item => $"{item.Difficulty}|{item.Shape}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item =>
            {
                if (!LogicalSuffixByShape.TryGetValue(
                        item.Shape,
                        out var suffix))
                {
                    throw new InvalidDataException(
                        $"No dial logical-name mapping exists for '{item.Shape}'.");
                }

                var colourName = item.Difficulty switch
                {
                    "Green" => "Green",
                    "White" => "White",
                    "Red" => "Red",
                    _ => throw new InvalidDataException(
                        $"Unsupported First Edition difficulty '{item.Difficulty}'.")
                };

                var repositoryPath = item.AssetPath
                    .Replace('\\', '/')
                    .TrimStart('/');

                return new FirstEditionDialLogicalAsset
                {
                    LogicalName = colourName + suffix,
                    Difficulty = item.Difficulty,
                    Shape = item.Shape,
                    SemanticKey = item.SemanticKey,
                    AssetId = item.AssetId,
                    RepositoryPath = repositoryPath,
                    Url = CombineUrl(assetBaseUrl, repositoryPath)
                };
            })
            .OrderBy(item => item.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicates = result
            .GroupBy(item => item.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidDataException(
                $"Duplicate dial logical assets: {string.Join(", ", duplicates)}");
        }

        foreach (var asset in result)
        {
            var localPath = Path.Combine(
                repositoryRoot,
                asset.RepositoryPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            ValidateFile(localPath, $"Registered manoeuvre icon '{asset.SemanticKey}'");
        }

        return result;
    }

    private static void PatchManeuverSetEditor(
        string path,
        IReadOnlyList<FirstEditionDialLogicalAsset> assets)
    {
        var text = File.ReadAllText(path);

        var colourStart = text.IndexOf(
            "local BLUE_COLOR",
            StringComparison.Ordinal);
        var rowMarker = text.IndexOf(
            "local ROW_Y",
            StringComparison.Ordinal);

        if (colourStart < 0 || rowMarker <= colourStart)
        {
            throw new InvalidDataException(
                "Could not locate ManeuverSetEditor asset constants.");
        }

        var replacement = BuildLuaAssetBlock(assets);
        text = text[..colourStart] + replacement + text[rowMarker..];

        text = text.Replace(
            "        local url = ASSET_BASE_URL .. file",
            "        local url = file",
            StringComparison.Ordinal);

        text = text.Replace(
            "        return \"Blue\"",
            "        return \"Green\"",
            StringComparison.Ordinal);

        text = text.Replace(
            "        return BLUE_COLOR",
            "        return GREEN_COLOR",
            StringComparison.Ordinal);

        // Remove Purple from accepted manoeuvre colours regardless of spacing.
        text = Regex.Replace(
            text,
            @"if\s+color\s*==\s*[\"']r[\"']\s+or\s+color\s*==\s*[\"']w[\"']\s+or\s+color\s*==\s*[\"']b[\"']\s+or\s+color\s*==\s*[\"']p[\"']\s+then",
            "if color == \"r\" or color == \"w\" or color == \"b\" then",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Terminate the colour cycle after Green/Blue-prefix rather than
        // entering Purple. The source Lua has varied indentation over time,
        // so use a formatting-independent replacement.
        text = Regex.Replace(
            text,
            @"elseif\s+current\s*==\s*[\"']b[\"']\s+then\s*return\s+[\"']p[\"']",
            "elseif current == \"b\" then\n        return nil",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Purple is not a First Edition manoeuvre difficulty.
        text = Regex.Replace(
            text,
            @"^\s*local\s+PURPLE_COLOR\s*=.*(?:\r?\n)?",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string BuildLuaAssetBlock(
        IReadOnlyList<FirstEditionDialLogicalAsset> assets)
    {
        var builder = new StringBuilder();

        builder.AppendLine("local GREEN_COLOR = \"#269b3fff\"");
        builder.AppendLine("local PURPLE_COLOR = \"#d000b3\"");
        builder.AppendLine("local ICON_ON = \"#ffffffff\"");
        builder.AppendLine("local ICON_OFF = \"#666666aa\"");
        builder.AppendLine(
            "local SPEED_ASSET_BASE_URL = \"https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/maneuvers/\"");
        builder.AppendLine();
        builder.AppendLine("local ASSET_FILES = {");
        builder.AppendLine("    Speed0 = SPEED_ASSET_BASE_URL .. \"v0.png\",");
        builder.AppendLine("    Speed1 = SPEED_ASSET_BASE_URL .. \"v1.png\",");
        builder.AppendLine("    Speed2 = SPEED_ASSET_BASE_URL .. \"v2.png\",");
        builder.AppendLine("    Speed3 = SPEED_ASSET_BASE_URL .. \"v3.png\",");
        builder.AppendLine("    Speed4 = SPEED_ASSET_BASE_URL .. \"v4.png\",");
        builder.AppendLine("    Speed5 = SPEED_ASSET_BASE_URL .. \"v5.png\",");
        builder.AppendLine();

        foreach (var asset in assets)
        {
            builder.Append("    ");
            builder.Append(asset.LogicalName);
            builder.Append(" = \"");
            builder.Append(EscapeLua(asset.Url));
            builder.AppendLine("\",");
        }

        builder.AppendLine("}");
        builder.AppendLine();

        return builder.ToString();
    }

    private static void PatchUnassignedDialXml(string path)
    {
        var text = File.ReadAllText(path);
        text = text.Replace(
            "image=\"Blue",
            "image=\"Green",
            StringComparison.Ordinal);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static RuntimeValidationResult ValidateGeneratedRuntime(
        string editorLuaPath,
        string unassignedXmlPath,
        IReadOnlyList<FirstEditionDialLogicalAsset> assets)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var lua = File.ReadAllText(editorLuaPath);
        var xml = File.ReadAllText(unassignedXmlPath);

        if (lua.Contains("local BLUE_COLOR", StringComparison.Ordinal))
            errors.Add("Generated ManeuverSetEditor.lua still defines BLUE_COLOR.");

        if (lua.Contains("return \"Blue\"", StringComparison.Ordinal))
            errors.Add("Runtime prefix b still resolves to Blue.");

        var purpleCycleMatch = Regex.Match(
            lua,
            @"return\s+[\"']p[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (purpleCycleMatch.Success)
        {
            var line = 1 + lua[..purpleCycleMatch.Index].Count(character => character == '\n');
            errors.Add(
                $"The manoeuvre editor still cycles into Purple at generated Lua line {line}.");
        }

        if (xml.Contains("image=\"Blue", StringComparison.Ordinal))
            errors.Add("Generated UnassignedDial.xml still references Blue manoeuvre assets.");

        foreach (var asset in assets)
        {
            if (!lua.Contains(
                    asset.LogicalName + " = \"" + asset.Url + "\"",
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"ManeuverSetEditor.lua does not install {asset.LogicalName}.");
            }
        }

        var expectedDefaults = new[]
        {
            "GreenStraight",
            "GreenBankL",
            "GreenBankR",
            "GreenTurnL",
            "GreenTurnR"
        };

        foreach (var name in expectedDefaults)
        {
            if (!assets.Any(asset =>
                    asset.LogicalName.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(
                    $"No registered green asset exists for expected basic icon {name}.");
            }
        }

        return new RuntimeValidationResult(errors, warnings);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory
                     .EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory
                     .EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                target,
                Path.GetRelativePath(source, file));

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static string ResolveContractPath(
        string repositoryRoot,
        string[] args)
    {
        var option = ReadOption(args, "--icon-contract");

        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12a",
                "maneuver-icon-registration",
                "first-edition-maneuver-icon-runtime-contract.json")
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
                "dial-runtime-integration")
            : Path.GetFullPath(option);
    }

    private static string ResolveAssetBaseUrl(string[] args)
    {
        var option = ReadOption(args, "--asset-base-url");

        return string.IsNullOrWhiteSpace(option)
            ? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/"
            : option.TrimEnd('/') + "/";
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

    private static string CombineUrl(string baseUrl, string relativePath) =>
        baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');

    private static string EscapeLua(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string NormalisePath(string path) =>
        path.Replace('\\', '/');

    private static void WriteCsv(
        string path,
        IEnumerable<FirstEditionDialLogicalAsset> assets)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "LogicalName,Difficulty,Shape,SemanticKey,AssetId,RepositoryPath,Url");

        foreach (var asset in assets)
        {
            writer.WriteLine(string.Join(',',
                Csv(asset.LogicalName),
                Csv(asset.Difficulty),
                Csv(asset.Shape),
                Csv(asset.SemanticKey),
                Csv(asset.AssetId),
                Csv(asset.RepositoryPath),
                Csv(asset.Url)));
        }
    }

    private static void WriteMarkdown(
        string path,
        FirstEditionDialRuntimeManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12A-3 – First Edition Dial Runtime Integration");
        writer.WriteLine();
        writer.WriteLine(
            $"Generated runtime: `{manifest.GeneratedRoot}`  ");
        writer.WriteLine(
            $"Asset base URL: `{manifest.AssetBaseUrl}`");
        writer.WriteLine();
        writer.WriteLine(
            "- Runtime prefix `b` now displays First Edition **Green**.");
        writer.WriteLine(
            "- White and Red retain their First Edition meanings.");
        writer.WriteLine(
            "- Purple is removed from the manoeuvre editor cycle.");
        writer.WriteLine(
            "- The Unified source runtime remains untouched.");
        writer.WriteLine();
        writer.WriteLine("| Logical asset | Difficulty | Shape | URL |");
        writer.WriteLine("|---|---|---|---|");

        foreach (var asset in manifest.Assets)
        {
            writer.WriteLine(
                $"| `{Md(asset.LogicalName)}` | {Md(asset.Difficulty)} | " +
                $"{Md(asset.Shape)} | `{Md(asset.Url)}` |");
        }

        if (manifest.ValidationWarnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Validation warnings");
            foreach (var warning in manifest.ValidationWarnings)
                writer.WriteLine($"- {Md(warning)}");
        }
    }

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
            "  build-first-edition-dial-runtime <first-edition-repository> " +
            "[--icon-contract <file>] [--asset-base-url <url>] [--output <folder>]");
    }

    private sealed record RuntimeValidationResult(
        List<string> Errors,
        List<string> Warnings);
}

public sealed class FirstEditionDialRuntimeManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string SourceDialFolder { get; init; } = string.Empty;
    public string GeneratedRoot { get; init; } = string.Empty;
    public string IconContractPath { get; init; } = string.Empty;
    public string AssetBaseUrl { get; init; } = string.Empty;
    public int RuntimeManeuverMappings { get; init; }
    public int LogicalIconAssets { get; init; }
    public List<string> GeneratedFiles { get; init; } = new();
    public List<string> ValidationErrors { get; init; } = new();
    public List<string> ValidationWarnings { get; init; } = new();
    public List<FirstEditionDialLogicalAsset> Assets { get; init; } = new();
}

public sealed class FirstEditionDialLogicalAsset
{
    public string LogicalName { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string SemanticKey { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class IconRuntimeContractInput
{
    public int AmbiguousMappings { get; init; }
    public int MissingMappings { get; init; }
    public List<IconLookupInput> Lookup { get; init; } = new();
}

public sealed class IconLookupInput
{
    public string RuntimeCode { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string SemanticKey { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string AssetPath { get; init; } = string.Empty;
}
