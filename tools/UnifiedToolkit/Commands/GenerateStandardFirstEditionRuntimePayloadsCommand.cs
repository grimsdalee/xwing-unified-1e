using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 11F-3:
/// Produces the stable runtime payload consumed by Phase 12.
///
/// The payload deliberately separates:
///   - actSet codes already understood by the reused Unified dial;
///   - First Edition actions that require explicit integration; and
///   - runtime metadata such as Rotate Arc and the independent Jam control.
///
/// Epic ships are excluded.
/// </summary>
public static class GenerateStandardFirstEditionRuntimePayloadsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, string> ReusedActionCodes =
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

    private static readonly HashSet<string> ExplicitFirstEditionActions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Coordinate",
            "Jam",
            "Reload",
            "Rotate Arc",
            "SLAM"
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
            var actionAnalysisPath = ResolveActionAnalysisPath(repositoryRoot, args);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);

            ValidateFile(runtimeDataPath, "Phase 11F-1 runtime data");
            ValidateFile(actionAnalysisPath, "Phase 11F-2 action analysis");

            var runtimeData = Read<RuntimeDataInput>(runtimeDataPath);
            var actionAnalysis = Read<ActionAnalysisInput>(actionAnalysisPath);

            var actionDispositions = actionAnalysis.TargetActions
                .ToDictionary(
                    item => item.OfficialAction,
                    item => item,
                    StringComparer.OrdinalIgnoreCase);

            var payloads = runtimeData.Records
                .Where(record =>
                    record.RuntimeType.Equals(
                        "Standard",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.ShipName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.ShipId, StringComparer.OrdinalIgnoreCase)
                .Select(record => BuildPayload(record, actionDispositions))
                .ToList();

            var manifest = new StandardRuntimePayloadManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                RuntimeDataPath = NormalisePath(runtimeDataPath),
                ActionAnalysisPath = NormalisePath(actionAnalysisPath),
                StandardShips = payloads.Count,
                MovementPayloadsReady = payloads.Count(
                    payload => payload.MoveSet.Count > 0
                        && payload.ValidationIssues.Count == 0),
                ActionPayloadsReady = payloads.Count(
                    payload => payload.UnknownActions.Count == 0),
                UnknownActions = payloads
                    .SelectMany(payload => payload.UnknownActions)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                InvalidPayloads = payloads.Count(
                    payload => payload.ValidationIssues.Count > 0),
                EpicShipsEmitted = 0,
                Payloads = payloads
            };

            Directory.CreateDirectory(outputFolder);

            var manifestPath = Path.Combine(
                outputFolder,
                "standard-first-edition-runtime-payloads.json");
            var csvPath = Path.Combine(
                outputFolder,
                "standard-first-edition-runtime-payloads.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "STANDARD-FIRST-EDITION-RUNTIME-PAYLOADS-REPORT.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, payloads);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 11F-3 Standard Runtime Payload Generation");
            Console.WriteLine(
                "==============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:               {repositoryRoot}");
            Console.WriteLine($"Runtime data:             {runtimeDataPath}");
            Console.WriteLine($"Action analysis:          {actionAnalysisPath}");
            Console.WriteLine();
            Console.WriteLine($"Standard ships:           {manifest.StandardShips}");
            Console.WriteLine($"Movement payloads ready:  {manifest.MovementPayloadsReady}");
            Console.WriteLine($"Action payloads ready:    {manifest.ActionPayloadsReady}");
            Console.WriteLine($"Unknown actions:          {manifest.UnknownActions.Count}");
            Console.WriteLine($"Invalid payloads:         {manifest.InvalidPayloads}");
            Console.WriteLine($"Epic ships emitted:       {manifest.EpicShipsEmitted}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                 {manifestPath}");
            Console.WriteLine($"CSV:                      {csvPath}");
            Console.WriteLine($"Report:                   {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Standard runtime payloads generated. No TTS objects were created.");

            return manifest.UnknownActions.Count == 0
                && manifest.InvalidPayloads == 0
                && manifest.StandardShips == 49
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Standard First Edition runtime-payload generation failed: {ex.Message}");
            return 1;
        }
    }

    private static StandardRuntimePayload BuildPayload(
        RuntimeShipInput record,
        IReadOnlyDictionary<string, ActionDispositionInput> dispositions)
    {
        var reusedActSet = new List<string>();
        var firstEditionActions = new List<string>();
        var unknownActions = new List<string>();
        var issues = new List<string>();
        var controls = new RuntimeControlRequirements();

        foreach (var action in record.OfficialActions
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ReusedActionCodes.TryGetValue(action, out var reusedCode))
            {
                reusedActSet.Add(reusedCode);
                continue;
            }

            if (!ExplicitFirstEditionActions.Contains(action))
            {
                unknownActions.Add(action);
                continue;
            }

            if (!dispositions.TryGetValue(action, out var disposition))
            {
                unknownActions.Add(action);
                continue;
            }

            firstEditionActions.Add(action);

            switch (action)
            {
                case "Jam":
                    controls = controls with
                    {
                        JamControl = disposition.Disposition.Equals(
                            "IndependentTokenControl",
                            StringComparison.OrdinalIgnoreCase)
                            ? "JamBtn/jamTok"
                            : "FirstEditionJamControl"
                    };
                    break;

                case "Rotate Arc":
                    controls = controls with
                    {
                        RotateArcMode = disposition.RuntimeCode ?? "Rot",
                        RotateArcAdditionalModes =
                            disposition.AdditionalRuntimeCodes.ToList()
                    };
                    break;

                case "Coordinate":
                    controls = controls with
                    {
                        CoordinateControl = "FirstEditionCoordinateControl"
                    };
                    break;

                case "Reload":
                    controls = controls with
                    {
                        ReloadControl = "FirstEditionReloadControl"
                    };
                    break;

                case "SLAM":
                    controls = controls with
                    {
                        SlamControl = "FirstEditionSlamControl"
                    };
                    break;
            }
        }

        if (record.MoveSet.Count == 0)
            issues.Add("The standard ship has no runtime maneuver payload.");

        if (record.Size.Equals(
                "medium",
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                "Medium base is not valid in First Edition.");
        }

        var baseSize = NormaliseBaseSize(record.Size);
        if (baseSize is not ("small" or "large"))
        {
            issues.Add(
                $"Standard ship has unsupported First Edition base size '{record.Size}'.");
        }

        if (record.Status != "Ready")
        {
            issues.Add(
                $"Phase 11F-1 runtime-data status is '{record.Status}'.");
        }

        return new StandardRuntimePayload
        {
            ShipId = record.ShipId,
            ShipName = record.ShipName,
            Factions = record.Factions,
            BaseSize = baseSize,
            RuntimeType = "Standard",
            MoveSet = record.MoveSet,
            ActSet = reusedActSet
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FirstEditionActions = firstEditionActions,
            RuntimeControls = controls,
            UnknownActions = unknownActions,
            ValidationIssues = issues,
            Source = new RuntimePayloadSource
            {
                MappingId = record.MappingId,
                SourceXws = record.SourceXws,
                SourceDial = record.SourceDial,
                OfficialActions = record.OfficialActions
            }
        };
    }

    private static string NormaliseBaseSize(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "small" => "small",
            "large" => "large",
            "medium" => "medium",
            _ => value.Trim().ToLowerInvariant()
        };

    private static RuntimeDataInput ReadRuntimeData(string path) =>
        Read<RuntimeDataInput>(path);

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

    private static string ResolveActionAnalysisPath(
        string repositoryRoot,
        string[] args)
    {
        var option = ReadOption(args, "--action-analysis");

        return string.IsNullOrWhiteSpace(option)
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase11f",
                "runtime-action-code-analysis",
                "runtime-action-code-analysis.json")
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
                "phase11f",
                "standard-runtime-payloads")
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

    private static void WriteCsv(
        string path,
        IEnumerable<StandardRuntimePayload> payloads)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "ShipId,ShipName,Factions,BaseSize,MoveSetCount,MoveSet," +
            "ActSet,FirstEditionActions,JamControl,RotateArcMode," +
            "RotateArcAdditionalModes,CoordinateControl,ReloadControl," +
            "SlamControl,UnknownActions,ValidationIssues");

        foreach (var payload in payloads)
        {
            writer.WriteLine(string.Join(',',
                Csv(payload.ShipId),
                Csv(payload.ShipName),
                Csv(string.Join('|', payload.Factions)),
                Csv(payload.BaseSize),
                payload.MoveSet.Count,
                Csv(string.Join('|', payload.MoveSet)),
                Csv(string.Join('|', payload.ActSet)),
                Csv(string.Join('|', payload.FirstEditionActions)),
                Csv(payload.RuntimeControls.JamControl ?? string.Empty),
                Csv(payload.RuntimeControls.RotateArcMode ?? string.Empty),
                Csv(string.Join(
                    '|',
                    payload.RuntimeControls.RotateArcAdditionalModes)),
                Csv(payload.RuntimeControls.CoordinateControl ?? string.Empty),
                Csv(payload.RuntimeControls.ReloadControl ?? string.Empty),
                Csv(payload.RuntimeControls.SlamControl ?? string.Empty),
                Csv(string.Join('|', payload.UnknownActions)),
                Csv(string.Join('|', payload.ValidationIssues))));
        }
    }

    private static void WriteMarkdown(
        string path,
        StandardRuntimePayloadManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 11F-3 – Standard First Edition Runtime Payloads");
        writer.WriteLine();
        writer.WriteLine(
            $"- Standard ships: **{manifest.StandardShips}**");
        writer.WriteLine(
            $"- Movement payloads ready: **{manifest.MovementPayloadsReady}**");
        writer.WriteLine(
            $"- Action payloads ready: **{manifest.ActionPayloadsReady}**");
        writer.WriteLine(
            $"- Unknown actions: **{manifest.UnknownActions.Count}**");
        writer.WriteLine(
            $"- Invalid payloads: **{manifest.InvalidPayloads}**");
        writer.WriteLine(
            $"- Epic ships emitted: **{manifest.EpicShipsEmitted}**");
        writer.WriteLine();
        writer.WriteLine(
            "`actSet` contains only action codes consumed by the reused " +
            "Unified dial. First Edition actions requiring new or independent " +
            "controls are retained in `firstEditionActions` and " +
            "`runtimeControls`.");
        writer.WriteLine();
        writer.WriteLine(
            "| Ship | Base | Maneuvers | actSet | First Edition controls | Status |");
        writer.WriteLine("|---|---|---:|---|---|---|");

        foreach (var payload in manifest.Payloads)
        {
            var controls = new List<string>();

            if (payload.RuntimeControls.JamControl is not null)
                controls.Add("Jam");
            if (payload.RuntimeControls.RotateArcMode is not null)
                controls.Add("Rotate Arc");
            if (payload.RuntimeControls.CoordinateControl is not null)
                controls.Add("Coordinate");
            if (payload.RuntimeControls.ReloadControl is not null)
                controls.Add("Reload");
            if (payload.RuntimeControls.SlamControl is not null)
                controls.Add("SLAM");

            var status = payload.ValidationIssues.Count == 0
                && payload.UnknownActions.Count == 0
                ? "Ready"
                : "Invalid";

            writer.WriteLine(
                $"| {Md(payload.ShipName)} (`{Md(payload.ShipId)}`) | " +
                $"{Md(payload.BaseSize)} | {payload.MoveSet.Count} | " +
                $"{Md(string.Join(", ", payload.ActSet))} | " +
                $"{Md(string.Join(", ", controls))} | {status} |");
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

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  generate-standard-first-edition-runtime-payloads " +
            "<first-edition-repository> [--runtime-data <file>] " +
            "[--action-analysis <file>] [--output <folder>]");
    }
}

public sealed class StandardRuntimePayloadManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string RuntimeDataPath { get; init; } = string.Empty;
    public string ActionAnalysisPath { get; init; } = string.Empty;
    public int StandardShips { get; init; }
    public int MovementPayloadsReady { get; init; }
    public int ActionPayloadsReady { get; init; }
    public List<string> UnknownActions { get; init; } = new();
    public int InvalidPayloads { get; init; }
    public int EpicShipsEmitted { get; init; }
    public List<StandardRuntimePayload> Payloads { get; init; } = new();
}

public sealed class StandardRuntimePayload
{
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public List<string> Factions { get; init; } = new();
    public string BaseSize { get; init; } = string.Empty;
    public string RuntimeType { get; init; } = string.Empty;
    public List<string> MoveSet { get; init; } = new();
    public List<string> ActSet { get; init; } = new();
    public List<string> FirstEditionActions { get; init; } = new();
    public RuntimeControlRequirements RuntimeControls { get; init; } = new();
    public List<string> UnknownActions { get; init; } = new();
    public List<string> ValidationIssues { get; init; } = new();
    public RuntimePayloadSource Source { get; init; } = new();
}

public sealed record RuntimeControlRequirements
{
    public string? JamControl { get; init; }
    public string? RotateArcMode { get; init; }
    public List<string> RotateArcAdditionalModes { get; init; } = new();
    public string? CoordinateControl { get; init; }
    public string? ReloadControl { get; init; }
    public string? SlamControl { get; init; }
}

public sealed class RuntimePayloadSource
{
    public string MappingId { get; init; } = string.Empty;
    public string SourceXws { get; init; } = string.Empty;
    public List<string> SourceDial { get; init; } = new();
    public List<string> OfficialActions { get; init; } = new();
}

public sealed class RuntimeDataInput
{
    public List<RuntimeShipInput> Records { get; init; } = new();
}

public sealed class RuntimeShipInput
{
    public string MappingId { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public List<string> Factions { get; init; } = new();
    public string RuntimeType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SourceXws { get; init; } = string.Empty;
    public List<string> SourceDial { get; init; } = new();
    public List<string> MoveSet { get; init; } = new();
    public List<string> OfficialActions { get; init; } = new();
}

public sealed class ActionAnalysisInput
{
    public List<ActionDispositionInput> TargetActions { get; init; } = new();
}

public sealed class ActionDispositionInput
{
    public string OfficialAction { get; init; } = string.Empty;
    public string? RuntimeCode { get; init; }
    public List<string> AdditionalRuntimeCodes { get; init; } = new();
    public string Disposition { get; init; } = string.Empty;
}
