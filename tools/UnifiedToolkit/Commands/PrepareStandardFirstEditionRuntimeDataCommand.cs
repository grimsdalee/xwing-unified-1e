using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 11F-1:
/// Converts official First Edition dial codes into the exact maneuver identifiers
/// consumed by the existing Unified dial runtime, and inventories action-bar
/// codes without modifying semantic mappings or TTS objects.
///
/// Epic ships are explicitly deferred.
/// </summary>
public static class PrepareStandardFirstEditionRuntimeDataCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> EpicShipIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "croccruiser",
        "gozanticlasscruiser",
        "gr75mediumtransport",
        "cr90corvette",
        "raiderclasscorvette"
    };

    private static readonly IReadOnlyDictionary<char, string> DifficultyPrefixes =
        new Dictionary<char, string>
        {
            ['G'] = "b", // First Edition green is displayed by the 2.5 runtime's blue maneuver controls.
            ['W'] = "w",
            ['R'] = "r"
        };

    private static readonly IReadOnlyDictionary<char, ManeuverBearing> Bearings =
        new Dictionary<char, ManeuverBearing>
        {
            ['T'] = new("TurnLeft", "tl", ""),
            ['B'] = new("BankLeft", "bl", ""),
            ['F'] = new("Straight", "s", ""),
            ['N'] = new("BankRight", "br", ""),
            ['Y'] = new("TurnRight", "tr", ""),
            ['K'] = new("KoiogranTurn", "k", ""),

            // First Edition Segnor's Loops are represented by the runtime as
            // bank maneuvers with the S suffix.
            ['L'] = new("SegnorsLoopLeft", "bl", "s"),
            ['R'] = new("SegnorsLoopRight", "br", "s"),

            // First Edition Tallon Rolls are represented by the runtime as
            // turn maneuvers with the T suffix.
            ['E'] = new("TallonRollLeft", "tl", "t"),
            ['P'] = new("TallonRollRight", "tr", "t"),

            // Reverse maneuvers use the ordinary bearing plus the R suffix.
            ['A'] = new("ReverseBankLeft", "bl", "r"),
            ['S'] = new("ReverseStraight", "s", "r"),
            ['D'] = new("ReverseBankRight", "br", "r"),

            // Stop is speed zero straight in the existing runtime controls.
            ['O'] = new("Stop", "s", "")
        };

    // These mappings were directly confirmed in the extracted Unified dial Lua.
    private static readonly IReadOnlyDictionary<string, string> VerifiedActionCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Focus"] = "F",
            ["Target Lock"] = "TL",
            ["Evade"] = "E",
            ["Reinforce"] = "R",
            ["Cloak"] = "CL",
            ["Barrel Roll"] = "BR",
            ["Boost"] = "B"
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
            var explicitXWingData = ResolveExplicitXWingData(args);
            var sourceLayout = FirstEditionDataSourceResolver.Resolve(
                repositoryRoot,
                explicitXWingData);

            var mappingFolder = ResolveMappingFolder(
                repositoryRoot,
                args,
                explicitXWingData is null ? 1 : 2);

            var outputFolder = ResolveOutputFolder(repositoryRoot, args);
            var mappingFile = Path.Combine(mappingFolder, "ships.json");
            var sourceFile = FindShipsDataFile(sourceLayout.DataRoot);

            ValidateFile(mappingFile, "First Edition ship mapping file");
            ValidateFile(sourceFile, "xwing-data ships source");

            var mappings = ReadMappings(mappingFile);
            var sourceShips = ReadSourceShips(sourceFile);
            var sourceIndex = BuildSourceIndex(sourceShips);

            var records = mappings
                .OrderBy(mapping => mapping.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mapping => mapping.TargetId, StringComparer.OrdinalIgnoreCase)
                .Select(mapping => BuildRecord(mapping, sourceIndex))
                .ToList();

            var standardRecords = records
                .Where(record => record.RuntimeType == "Standard")
                .ToList();

            var epicRecords = records
                .Where(record => record.RuntimeType == "EpicDeferred")
                .ToList();

            var invalidRecords = standardRecords
                .Where(record => record.Status != "Ready")
                .ToList();

            Directory.CreateDirectory(outputFolder);

            var manifest = new StandardRuntimeDataManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                XWingDataRoot = NormalisePath(sourceLayout.DataRoot),
                MappingFolder = NormalisePath(mappingFolder),
                MappingFile = NormalisePath(mappingFile),
                SourceFile = NormalisePath(sourceFile),
                SemanticShipCount = mappings.Count,
                StandardShipCount = standardRecords.Count,
                EpicDeferredCount = epicRecords.Count,
                ReadyStandardShips = standardRecords.Count(record => record.Status == "Ready"),
                InvalidStandardShips = invalidRecords.Count,
                UniqueSourceDialCodes = records
                    .SelectMany(record => record.SourceDial)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                UniqueRuntimeManeuvers = standardRecords
                    .SelectMany(record => record.MoveSet)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                UnsupportedStandardActions = standardRecords
                    .SelectMany(record => record.UnverifiedActions)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Records = records
            };

            var manifestPath = Path.Combine(
                outputFolder,
                "standard-first-edition-runtime-data.json");
            var csvPath = Path.Combine(
                outputFolder,
                "standard-first-edition-runtime-data.csv");
            var actionCsvPath = Path.Combine(
                outputFolder,
                "standard-first-edition-action-inventory.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "STANDARD-FIRST-EDITION-RUNTIME-DATA-REPORT.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteRuntimeCsv(csvPath, records);
            WriteActionCsv(actionCsvPath, standardRecords);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 11F-1 Standard Runtime Data Preparation");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:       {repositoryRoot}");
            Console.WriteLine($"xwing-data:       {sourceLayout.DataRoot}");
            Console.WriteLine($"Mapping folder:   {mappingFolder}");
            Console.WriteLine($"Source file:      {sourceFile}");
            Console.WriteLine();
            Console.WriteLine($"Semantic ships:                  {mappings.Count}");
            Console.WriteLine($"Standard Small/Large ships:      {standardRecords.Count}");
            Console.WriteLine($"Standard maneuver sets ready:    {manifest.ReadyStandardShips}");
            Console.WriteLine($"Invalid standard maneuver sets:  {manifest.InvalidStandardShips}");
            Console.WriteLine($"Epic ships deferred:             {epicRecords.Count}");
            Console.WriteLine($"Unique source dial codes:        {manifest.UniqueSourceDialCodes.Count}");
            Console.WriteLine($"Unique runtime maneuver IDs:     {manifest.UniqueRuntimeManeuvers.Count}");
            Console.WriteLine($"Unverified standard actions:     {manifest.UnsupportedStandardActions.Count}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:      {manifestPath}");
            Console.WriteLine($"Runtime CSV:   {csvPath}");
            Console.WriteLine($"Action CSV:    {actionCsvPath}");
            Console.WriteLine($"Report:        {reportPath}");
            Console.WriteLine();

            if (manifest.UnsupportedStandardActions.Count > 0)
            {
                Console.WriteLine(
                    "Maneuver translation is complete. The action inventory contains " +
                    "runtime codes that still require confirmation before payload generation:");
                foreach (var action in manifest.UnsupportedStandardActions)
                    Console.WriteLine($"  - {action}");
                Console.WriteLine();
            }

            Console.WriteLine(
                "Runtime source data prepared. No semantic mappings or TTS objects were modified.");

            return manifest.InvalidStandardShips == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Standard First Edition runtime-data preparation failed: {ex.Message}");
            return 1;
        }
    }

    private static StandardRuntimeShipRecord BuildRecord(
        ShipMappingRecord mapping,
        IReadOnlyDictionary<string, SourceShipRecord> sourceIndex)
    {
        var targetId = Normalise(mapping.TargetId);
        var isEpic = EpicShipIds.Contains(targetId)
            || mapping.Size.Equals("epic", StringComparison.OrdinalIgnoreCase)
            || mapping.Size.Equals("huge", StringComparison.OrdinalIgnoreCase);

        var source = FindSource(mapping, sourceIndex);

        if (isEpic)
        {
            return new StandardRuntimeShipRecord
            {
                MappingId = mapping.MappingId,
                ShipId = mapping.TargetId,
                ShipName = mapping.Name,
                Size = mapping.Size,
                Factions = mapping.Factions,
                RuntimeType = "EpicDeferred",
                Status = "Deferred",
                SourceXws = source?.Xws ?? string.Empty,
                SourceName = source?.Name ?? string.Empty,
                SourceDial = source?.Dial ?? new List<string>(),
                OfficialActions = source?.Actions ?? new List<string>(),
                Notes = "Epic ship runtime and movement are explicitly deferred to the later Epic phase."
            };
        }

        if (source is null)
        {
            return new StandardRuntimeShipRecord
            {
                MappingId = mapping.MappingId,
                ShipId = mapping.TargetId,
                ShipName = mapping.Name,
                Size = mapping.Size,
                Factions = mapping.Factions,
                RuntimeType = "Standard",
                Status = "MissingSource",
                Notes = "No matching xwing-data ship record was found."
            };
        }

        var translated = new List<RuntimeManeuverRecord>();
        var issues = new List<string>();

        foreach (var sourceCode in source.Dial)
        {
            if (TryTranslateManeuver(sourceCode, out var maneuver, out var issue))
                translated.Add(maneuver!);
            else
                issues.Add(issue);
        }

        var actionRecords = source.Actions
            .Select(action => new RuntimeActionRecord
            {
                OfficialName = action,
                RuntimeCode = VerifiedActionCodes.TryGetValue(action, out var code)
                    ? code
                    : null,
                Verification = VerifiedActionCodes.ContainsKey(action)
                    ? "VerifiedFromDialLua"
                    : "RequiresRuntimeConfirmation"
            })
            .ToList();

        if (source.Dial.Count == 0)
            issues.Add("Source ship record has no standard dial array.");

        if (translated
            .Select(item => item.RuntimeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != translated.Count)
        {
            issues.Add("Two or more source dial codes translated to the same runtime maneuver ID.");
        }

        return new StandardRuntimeShipRecord
        {
            MappingId = mapping.MappingId,
            ShipId = mapping.TargetId,
            ShipName = mapping.Name,
            Size = mapping.Size,
            Factions = mapping.Factions,
            RuntimeType = "Standard",
            Status = issues.Count == 0 ? "Ready" : "Invalid",
            SourceXws = source.Xws,
            SourceName = source.Name,
            SourceDial = source.Dial,
            Maneuvers = translated,
            MoveSet = translated.Select(item => item.RuntimeCode).ToList(),
            OfficialActions = source.Actions,
            Actions = actionRecords,
            ActSet = actionRecords
                .Where(item => item.RuntimeCode is not null)
                .Select(item => item.RuntimeCode!)
                .ToList(),
            UnverifiedActions = actionRecords
                .Where(item => item.RuntimeCode is null)
                .Select(item => item.OfficialName)
                .ToList(),
            Issues = issues,
            Notes = issues.Count == 0
                ? "All First Edition dial codes translated to existing Unified runtime maneuver identifiers."
                : "One or more source records could not be translated safely."
        };
    }

    private static bool TryTranslateManeuver(
        string sourceCode,
        out RuntimeManeuverRecord? record,
        out string issue)
    {
        record = null;
        issue = string.Empty;

        var code = (sourceCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 3 || !char.IsDigit(code[0]))
        {
            issue = $"Unsupported dial code format: '{sourceCode}'.";
            return false;
        }

        var speed = code[0] - '0';
        var bearingCode = code[1];
        var difficultyCode = code[2];

        if (!Bearings.TryGetValue(bearingCode, out var bearing))
        {
            issue = $"Unknown bearing '{bearingCode}' in dial code '{sourceCode}'.";
            return false;
        }

        if (!DifficultyPrefixes.TryGetValue(difficultyCode, out var prefix))
        {
            issue = $"Unknown difficulty '{difficultyCode}' in dial code '{sourceCode}'.";
            return false;
        }

        if (bearingCode == 'O' && speed != 0)
        {
            issue = $"Stop maneuver must use speed zero: '{sourceCode}'.";
            return false;
        }

        if (bearingCode != 'O' && speed == 0)
        {
            issue = $"Only the stop maneuver may use speed zero: '{sourceCode}'.";
            return false;
        }

        var runtimeCode = $"{prefix}{bearing.RuntimeBearing}{speed}{bearing.RuntimeSuffix}";

        record = new RuntimeManeuverRecord
        {
            SourceCode = code,
            Speed = speed,
            BearingCode = bearingCode.ToString(),
            Bearing = bearing.Name,
            DifficultyCode = difficultyCode.ToString(),
            Difficulty = difficultyCode switch
            {
                'G' => "Green",
                'W' => "White",
                'R' => "Red",
                _ => "Unknown"
            },
            RuntimeDifficulty = prefix switch
            {
                "b" => "BlueControlForFirstEditionGreen",
                "w" => "White",
                "r" => "Red",
                _ => "Unknown"
            },
            RuntimeCode = runtimeCode
        };

        return true;
    }

    private static SourceShipRecord? FindSource(
        ShipMappingRecord mapping,
        IReadOnlyDictionary<string, SourceShipRecord> sourceIndex)
    {
        var keys = new[]
        {
            Normalise(mapping.TargetId),
            Normalise(mapping.SourceId),
            Normalise(mapping.Name)
        }
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            if (sourceIndex.TryGetValue(key, out var source))
                return source;
        }

        return null;
    }

    private static Dictionary<string, SourceShipRecord> BuildSourceIndex(
        IEnumerable<SourceShipRecord> ships)
    {
        var result = new Dictionary<string, SourceShipRecord>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var ship in ships)
        {
            AddIndex(result, ship.Xws, ship);
            AddIndex(result, Normalise(ship.Name), ship);
        }

        return result;
    }

    private static void AddIndex(
        IDictionary<string, SourceShipRecord> index,
        string key,
        SourceShipRecord value)
    {
        if (key.Length > 0 && !index.ContainsKey(key))
            index.Add(key, value);
    }

    private static List<ShipMappingRecord> ReadMappings(string path)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            JsonOptionsForSource());

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ships.json must contain a JSON array.");

        return document.RootElement
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ShipMappingRecord
            {
                MappingId = ReadString(item, "mappingId"),
                SourceId = ReadString(item, "sourceId"),
                TargetId = ReadString(item, "targetId"),
                Name = ReadString(item, "name"),
                Size = ReadString(item, "size"),
                Factions = ReadStringArray(item, "factions")
            })
            .Where(item => item.TargetId.Length > 0)
            .ToList();
    }

    private static List<SourceShipRecord> ReadSourceShips(string path)
    {
        var raw = File.ReadAllText(path);
        var json = UnwrapJavaScriptArray(raw);

        using var document = JsonDocument.Parse(json, JsonOptionsForSource());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                "The xwing-data ship source must contain an array.");

        var result = new List<SourceShipRecord>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = ReadString(item, "name");
            var xws = FirstNonEmpty(
                ReadString(item, "xws"),
                ReadString(item, "id"),
                Normalise(name));

            if (name.Length == 0 || xws.Length == 0)
                continue;

            result.Add(new SourceShipRecord
            {
                Xws = Normalise(xws),
                Name = name,
                Size = Normalise(ReadString(item, "size")),
                Factions = ReadStringArray(item, "faction", "factions")
                    .Select(Normalise)
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Actions = ReadStringArray(item, "actions"),
                Dial = ReadStringArray(item, "dial")
            });
        }

        return result;
    }

    private static string FindShipsDataFile(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "data", "ships.js"),
            Path.Combine(root, "data", "ships.json"),
            Path.Combine(root, "ships.js"),
            Path.Combine(root, "ships.json")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "Could not locate ships.js or ships.json under the xwing-data source.");
    }

    private static string? ResolveExplicitXWingData(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            return null;

        var candidate = Path.GetFullPath(args[1]);
        return FirstEditionDataSourceResolver.LooksLikeDataSource(candidate)
            ? candidate
            : null;
    }

    private static string ResolveMappingFolder(
        string repositoryRoot,
        string[] args,
        int positionalStart)
    {
        var explicitOption = ReadOption(args, "--mapping-folder");
        if (!string.IsNullOrWhiteSpace(explicitOption))
            return Path.GetFullPath(explicitOption);

        var positional = args
            .Skip(positionalStart)
            .FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(positional))
            return Path.GetFullPath(positional);

        var candidates = new[]
        {
            Path.Combine(
                repositoryRoot,
                "tools",
                "UnifiedToolkit",
                "ConversionData",
                "first-edition"),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "ConversionData",
                "first-edition"),
            Path.Combine(
                AppContext.BaseDirectory,
                "ConversionData",
                "first-edition")
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static string ResolveOutputFolder(
        string repositoryRoot,
        string[] args)
    {
        var explicitOption = ReadOption(args, "--output");

        return string.IsNullOrWhiteSpace(explicitOption)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase11f",
                "standard-runtime-data")
            : Path.GetFullPath(explicitOption);
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

    private static void WriteRuntimeCsv(
        string path,
        IEnumerable<StandardRuntimeShipRecord> records)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "Status,RuntimeType,ShipId,ShipName,Size,Factions,SourceXws," +
            "SourceDialCount,SourceDial,MoveSetCount,MoveSet,OfficialActions," +
            "ActSet,UnverifiedActions,Issues,Notes");

        foreach (var record in records)
        {
            writer.WriteLine(string.Join(',',
                Csv(record.Status),
                Csv(record.RuntimeType),
                Csv(record.ShipId),
                Csv(record.ShipName),
                Csv(record.Size),
                Csv(string.Join('|', record.Factions)),
                Csv(record.SourceXws),
                record.SourceDial.Count,
                Csv(string.Join('|', record.SourceDial)),
                record.MoveSet.Count,
                Csv(string.Join('|', record.MoveSet)),
                Csv(string.Join('|', record.OfficialActions)),
                Csv(string.Join('|', record.ActSet)),
                Csv(string.Join('|', record.UnverifiedActions)),
                Csv(string.Join('|', record.Issues)),
                Csv(record.Notes)));
        }
    }

    private static void WriteActionCsv(
        string path,
        IEnumerable<StandardRuntimeShipRecord> records)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "ShipId,ShipName,OfficialAction,RuntimeCode,Verification");

        foreach (var record in records)
        {
            foreach (var action in record.Actions)
            {
                writer.WriteLine(string.Join(',',
                    Csv(record.ShipId),
                    Csv(record.ShipName),
                    Csv(action.OfficialName),
                    Csv(action.RuntimeCode ?? string.Empty),
                    Csv(action.Verification)));
            }
        }
    }

    private static void WriteMarkdown(
        string path,
        StandardRuntimeDataManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine("# Phase 11F-1 – Standard First Edition Runtime Data");
        writer.WriteLine();
        writer.WriteLine($"- Semantic ships: **{manifest.SemanticShipCount}**");
        writer.WriteLine($"- Standard Small/Large ships: **{manifest.StandardShipCount}**");
        writer.WriteLine($"- Standard maneuver sets ready: **{manifest.ReadyStandardShips}**");
        writer.WriteLine($"- Invalid standard maneuver sets: **{manifest.InvalidStandardShips}**");
        writer.WriteLine($"- Epic ships deferred: **{manifest.EpicDeferredCount}**");
        writer.WriteLine($"- Unique source dial codes: **{manifest.UniqueSourceDialCodes.Count}**");
        writer.WriteLine($"- Unique runtime maneuver IDs: **{manifest.UniqueRuntimeManeuvers.Count}**");
        writer.WriteLine();
        writer.WriteLine(
            "First Edition green maneuvers intentionally use the existing runtime's " +
            "blue-control prefix (`b`). This preserves the proven movement engine while " +
            "the visual icon layer is converted to First Edition artwork.");
        writer.WriteLine();
        writer.WriteLine("## Action codes requiring confirmation");
        writer.WriteLine();

        if (manifest.UnsupportedStandardActions.Count == 0)
        {
            writer.WriteLine("None.");
        }
        else
        {
            foreach (var action in manifest.UnsupportedStandardActions)
                writer.WriteLine($"- `{action}`");
        }

        writer.WriteLine();
        writer.WriteLine("## Ship coverage");
        writer.WriteLine();
        writer.WriteLine("| Status | Runtime | Ship | Dial | Runtime moveSet | Unverified actions |");
        writer.WriteLine("|---|---|---|---:|---:|---|");

        foreach (var record in manifest.Records)
        {
            writer.WriteLine(
                $"| {Md(record.Status)} | {Md(record.RuntimeType)} | " +
                $"{Md(record.ShipName)} (`{Md(record.ShipId)}`) | " +
                $"{record.SourceDial.Count} | {record.MoveSet.Count} | " +
                $"{Md(string.Join(", ", record.UnverifiedActions))} |");
        }
    }

    private static JsonDocumentOptions JsonOptionsForSource() => new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static string UnwrapJavaScriptArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');

        if (start < 0 || end <= start)
            throw new InvalidDataException(
                "Could not locate a JSON array in the xwing-data ships source.");

        return text[start..(end + 1)];
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static List<string> ReadStringArray(
        JsonElement element,
        params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!element.TryGetProperty(property, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return new List<string> { value.GetString() ?? string.Empty };

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => item.Length > 0)
                    .ToList();
            }
        }

        return new List<string>();
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static string Normalise(string value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalisePath(string value) =>
        value.Replace('\\', '/');

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
            "  prepare-standard-first-edition-runtime-data " +
            "<first-edition-repository> [xwing-data-folder] [mapping-folder] " +
            "[--output <folder>]");
    }

    private sealed record ManeuverBearing(
        string Name,
        string RuntimeBearing,
        string RuntimeSuffix);

    private sealed class ShipMappingRecord
    {
        public string MappingId { get; init; } = string.Empty;
        public string SourceId { get; init; } = string.Empty;
        public string TargetId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Size { get; init; } = string.Empty;
        public List<string> Factions { get; init; } = new();
    }

    private sealed class SourceShipRecord
    {
        public string Xws { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Size { get; init; } = string.Empty;
        public List<string> Factions { get; init; } = new();
        public List<string> Actions { get; init; } = new();
        public List<string> Dial { get; init; } = new();
    }
}

public sealed class StandardRuntimeDataManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string XWingDataRoot { get; init; } = string.Empty;
    public string MappingFolder { get; init; } = string.Empty;
    public string MappingFile { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int SemanticShipCount { get; init; }
    public int StandardShipCount { get; init; }
    public int EpicDeferredCount { get; init; }
    public int ReadyStandardShips { get; init; }
    public int InvalidStandardShips { get; init; }
    public List<string> UniqueSourceDialCodes { get; init; } = new();
    public List<string> UniqueRuntimeManeuvers { get; init; } = new();
    public List<string> UnsupportedStandardActions { get; init; } = new();
    public List<StandardRuntimeShipRecord> Records { get; init; } = new();
}

public sealed class StandardRuntimeShipRecord
{
    public string MappingId { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public List<string> Factions { get; init; } = new();
    public string RuntimeType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SourceXws { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public List<string> SourceDial { get; init; } = new();
    public List<RuntimeManeuverRecord> Maneuvers { get; init; } = new();
    public List<string> MoveSet { get; init; } = new();
    public List<string> OfficialActions { get; init; } = new();
    public List<RuntimeActionRecord> Actions { get; init; } = new();
    public List<string> ActSet { get; init; } = new();
    public List<string> UnverifiedActions { get; init; } = new();
    public List<string> Issues { get; init; } = new();
    public string Notes { get; init; } = string.Empty;
}

public sealed class RuntimeManeuverRecord
{
    public string SourceCode { get; init; } = string.Empty;
    public int Speed { get; init; }
    public string BearingCode { get; init; } = string.Empty;
    public string Bearing { get; init; } = string.Empty;
    public string DifficultyCode { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string RuntimeDifficulty { get; init; } = string.Empty;
    public string RuntimeCode { get; init; } = string.Empty;
}

public sealed class RuntimeActionRecord
{
    public string OfficialName { get; init; } = string.Empty;
    public string? RuntimeCode { get; init; }
    public string Verification { get; init; } = string.Empty;
}
