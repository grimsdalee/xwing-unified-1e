using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 11F-2:
/// Audits the actual action-code contract used by Unified.
///
/// PilotDb.lua stores runtime action arrays in action_set, not actSet.
/// The assigned dial later receives the same values as shipData.actSet.
///
/// This command distinguishes:
///   - codes directly consumed by the dial UI;
///   - codes present in PilotDb but handled by another runtime subsystem;
///   - controls that exist independently of actSet; and
///   - First Edition actions for which Unified has no reusable actSet code.
///
/// It never aliases a 2.5 action to a different First Edition action. In
/// particular, C is Calculate and must not be reused for Coordinate.
/// </summary>
public static class AnalyseRuntimeActionCodesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex PilotBlockRegex = new(
        @"masterPilotDB\s*\[\s*['""](?<id>[^'""]+)['""]\s*\]\s*=\s*\{(?<body>.*?)(?=masterPilotDB\s*\[|\z)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline |
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PilotNameRegex = new(
        @"\bname\s*=\s*['""](?<name>[^'""]+)['""]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    private static readonly Regex ActionSetRegex = new(
        @"(?:\baction_set\b|\[\s*['""]action_set['""]\s*\])\s*=\s*\{(?<body>.*?)\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline |
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex QuotedValueRegex = new(
        @"['""](?<value>[^'""]+)['""]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DialActionLoopRegex = new(
        @"for\s+_,\s*v\s+in\s+pairs\s*\(\s*shipData\s*\[\s*['""]actSet['""]\s*\]\s*\)\s*do(?<body>.*?)\n\s*end",
        RegexOptions.IgnoreCase | RegexOptions.Singleline |
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DialActionBranchRegex = new(
        @"(?:if|elseif)\s+v\s*==\s*['""](?<code>[^'""]+)['""]\s+then(?<body>.*?)(?=(?:elseif\s+v\s*==)|\n\s*end)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline |
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] TargetActions =
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
            var unifiedRoot = ResolveUnifiedRoot(repositoryRoot);
            var pilotDbPath = FindPilotDb(unifiedRoot);
            var dialLuaFiles = FindDialLuaFiles(unifiedRoot);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);

            ValidateFile(pilotDbPath, "Unified PilotDb.lua");

            var pilotText = File.ReadAllText(pilotDbPath);
            var pilots = ParsePilots(pilotText);
            var inventory = BuildCodeInventory(pilots);

            var dialBranches = new List<DialActionBranch>();
            var luaUsage = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in dialLuaFiles)
            {
                var text = File.ReadAllText(file);
                dialBranches.AddRange(ParseDialBranches(file, text));

                foreach (var code in inventory.Select(item => item.RuntimeCode))
                {
                    if (!ContainsQuotedCode(text, code))
                        continue;

                    if (!luaUsage.TryGetValue(code, out var files))
                    {
                        files = new List<string>();
                        luaUsage.Add(code, files);
                    }

                    files.Add(NormalisePath(file));
                }
            }

            var allLuaFiles = Directory
                .EnumerateFiles(unifiedRoot, "*.lua", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var repositoryUsage = ScanRepositoryUsage(allLuaFiles, inventory);
            var targets = BuildTargetResults(
                inventory,
                dialBranches,
                repositoryUsage,
                dialLuaFiles);

            Directory.CreateDirectory(outputFolder);

            var manifest = new RuntimeActionCodeAnalysisManifest
            {
                SchemaVersion = "2.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                UnifiedRoot = NormalisePath(unifiedRoot),
                PilotDbPath = NormalisePath(pilotDbPath),
                PilotRecordsParsed = pilots.Count,
                PilotRecordsWithActionSet = pilots.Count(
                    pilot => pilot.ActionSet.Count > 0),
                UniqueRuntimeCodes = inventory.Count,
                DialLuaFilesScanned = dialLuaFiles.Count,
                RuntimeCodes = inventory,
                DialBranches = dialBranches
                    .GroupBy(
                        branch => branch.RuntimeCode,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(
                        branch => branch.RuntimeCode,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                TargetActions = targets,
                ReusableTargetMappings = targets.Count(
                    target => target.Disposition == "ReusableActSetCode"),
                RuntimeExtensionsRequired = targets.Count(
                    target => target.Disposition != "ReusableActSetCode")
            };

            var manifestPath = Path.Combine(
                outputFolder,
                "runtime-action-code-analysis.json");
            var inventoryCsvPath = Path.Combine(
                outputFolder,
                "runtime-action-code-inventory.csv");
            var targetCsvPath = Path.Combine(
                outputFolder,
                "first-edition-action-runtime-disposition.csv");
            var reportPath = Path.Combine(
                outputFolder,
                "RUNTIME-ACTION-CODE-ANALYSIS-REPORT.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteInventoryCsv(inventoryCsvPath, inventory);
            WriteTargetCsv(targetCsvPath, targets);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 11F-2 Runtime Action Code Analysis");
            Console.WriteLine(
                "========================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                  {repositoryRoot}");
            Console.WriteLine($"Unified source:              {unifiedRoot}");
            Console.WriteLine($"PilotDb.lua:                 {pilotDbPath}");
            Console.WriteLine();
            Console.WriteLine($"Pilot records parsed:        {pilots.Count}");
            Console.WriteLine(
                $"Pilots with action_set:       {manifest.PilotRecordsWithActionSet}");
            Console.WriteLine($"Unique runtime action codes: {inventory.Count}");
            Console.WriteLine($"Dial Lua files scanned:      {dialLuaFiles.Count}");
            Console.WriteLine();
            Console.WriteLine("First Edition action disposition:");

            foreach (var target in targets)
            {
                Console.WriteLine(
                    $"  {target.OfficialAction,-12} -> " +
                    $"{target.RuntimeCode ?? "<none>",-8} " +
                    $"{target.Disposition}");
            }

            Console.WriteLine();
            Console.WriteLine(
                $"Reusable actSet mappings:      {manifest.ReusableTargetMappings}");
            Console.WriteLine(
                $"Runtime/UI extensions needed:  {manifest.RuntimeExtensionsRequired}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                    {manifestPath}");
            Console.WriteLine($"Code inventory:              {inventoryCsvPath}");
            Console.WriteLine($"Target dispositions:         {targetCsvPath}");
            Console.WriteLine($"Report:                      {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Action-code analysis completed. No mappings or TTS objects were modified.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Runtime action-code analysis failed: {ex.Message}");
            return 1;
        }
    }

    private static List<PilotActionSetRecord> ParsePilots(string source)
    {
        var results = new List<PilotActionSetRecord>();

        foreach (Match match in PilotBlockRegex.Matches(source))
        {
            var body = match.Groups["body"].Value;
            var actionMatch = ActionSetRegex.Match(body);
            var actions = actionMatch.Success
                ? QuotedValueRegex.Matches(
                        actionMatch.Groups["body"].Value)
                    .Cast<Match>()
                    .Select(item => item.Groups["value"].Value.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            var nameMatch = PilotNameRegex.Match(body);

            results.Add(new PilotActionSetRecord
            {
                PilotId = match.Groups["id"].Value,
                PilotName = nameMatch.Success
                    ? nameMatch.Groups["name"].Value
                    : match.Groups["id"].Value,
                ActionSet = actions
            });
        }

        return results;
    }

    private static List<RuntimeActionCodeInventory> BuildCodeInventory(
        IReadOnlyList<PilotActionSetRecord> pilots)
    {
        return pilots
            .SelectMany(
                pilot => pilot.ActionSet.Select(
                    code => new { Pilot = pilot, Code = code }))
            .GroupBy(
                item => item.Code,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new RuntimeActionCodeInventory
            {
                RuntimeCode = group.Key,
                PilotCount = group
                    .Select(item => item.Pilot.PilotId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                ExamplePilots = group
                    .Select(item => item.Pilot.PilotName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList()
            })
            .OrderBy(
                item => item.RuntimeCode,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DialActionBranch> ParseDialBranches(
        string file,
        string text)
    {
        var loop = DialActionLoopRegex.Match(text);
        if (!loop.Success)
            return new List<DialActionBranch>();

        return DialActionBranchRegex.Matches(loop.Groups["body"].Value)
            .Cast<Match>()
            .Select(match =>
            {
                var body = match.Groups["body"].Value;
                var controls = Regex.Matches(
                        body,
                        @"(?:setAttribute\s*\(\s*['""](?<id>[^'""]+)['""]|button\.(?<method>[A-Za-z0-9_]+))",
                        RegexOptions.IgnoreCase |
                        RegexOptions.CultureInvariant)
                    .Cast<Match>()
                    .Select(value =>
                        value.Groups["id"].Success
                            ? value.Groups["id"].Value
                            : value.Groups["method"].Value)
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new DialActionBranch
                {
                    RuntimeCode = match.Groups["code"].Value,
                    File = NormalisePath(file),
                    UiControls = controls,
                    Meaning = InferBranchMeaning(controls)
                };
            })
            .ToList();
    }

    private static string? InferBranchMeaning(
        IReadOnlyCollection<string> controls)
    {
        var text = string.Join(" ", controls);

        if (text.Contains("Focus", StringComparison.OrdinalIgnoreCase))
            return "Focus";
        if (text.Contains("TargetLock", StringComparison.OrdinalIgnoreCase))
            return "Target Lock";
        if (text.Contains("Evade", StringComparison.OrdinalIgnoreCase))
            return "Evade";
        if (text.Contains("Reinforce", StringComparison.OrdinalIgnoreCase))
            return "Reinforce";
        if (text.Contains("Calculate", StringComparison.OrdinalIgnoreCase))
            return "Calculate";
        if (text.Contains("Cloak", StringComparison.OrdinalIgnoreCase))
            return "Cloak";
        if (text.Contains("BarrelRoll", StringComparison.OrdinalIgnoreCase))
            return "Barrel Roll";
        if (text.Contains("Boost", StringComparison.OrdinalIgnoreCase))
            return "Boost";
        if (text.Contains("Aileron", StringComparison.OrdinalIgnoreCase))
            return "Adaptive Ailerons";
        if (text.Contains("Pivot", StringComparison.OrdinalIgnoreCase))
            return "Pivot Wing";
        if (text.Contains("Viper", StringComparison.OrdinalIgnoreCase))
            return "StarViper Barrel Roll";
        if (text.Contains("TurnBarrelRoll", StringComparison.OrdinalIgnoreCase))
            return "Turn Barrel Roll";
        if (text.Contains("Nantex", StringComparison.OrdinalIgnoreCase))
            return "Pinpoint Tractor Array";

        return null;
    }

    private static List<FirstEditionActionRuntimeDisposition>
        BuildTargetResults(
            IReadOnlyList<RuntimeActionCodeInventory> inventory,
            IReadOnlyList<DialActionBranch> branches,
            IReadOnlyDictionary<string, List<string>> repositoryUsage,
            IReadOnlyList<string> dialLuaFiles)
    {
        var codes = inventory
            .Select(item => item.RuntimeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var branchByMeaning = branches
            .Where(branch => !string.IsNullOrWhiteSpace(branch.Meaning))
            .GroupBy(
                branch => branch.Meaning!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var results = new List<FirstEditionActionRuntimeDisposition>();

        // Coordinate cannot use C: the actual dial branch proves C is Calculate.
        results.Add(new FirstEditionActionRuntimeDisposition
        {
            OfficialAction = "Coordinate",
            RuntimeCode = null,
            Disposition = "RuntimeExtensionRequired",
            Evidence = new List<string>
            {
                branchByMeaning.TryGetValue(
                    "Calculate",
                    out var calculate)
                    ? $"Runtime code C activates {string.Join(", ", calculate.UiControls)}."
                    : "Runtime code C is used by Calculate in Unified.",
                "PilotDb contains no distinct Coordinate action_set code.",
                "A First Edition Coordinate control/icon must be added rather than aliasing Calculate."
            }
        });

        var jamFiles = dialLuaFiles
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains(
                        "JamBtn",
                        StringComparison.OrdinalIgnoreCase)
                    || text.Contains(
                        "jamTok",
                        StringComparison.OrdinalIgnoreCase);
            })
            .Select(NormalisePath)
            .ToList();

        results.Add(new FirstEditionActionRuntimeDisposition
        {
            OfficialAction = "Jam",
            RuntimeCode = null,
            Disposition = jamFiles.Count > 0
                ? "IndependentTokenControl"
                : "RuntimeExtensionRequired",
            Evidence = jamFiles.Count > 0
                ? new List<string>
                {
                    "The dial already exposes JamBtn/jamTok independently of shipData.actSet.",
                    $"Evidence files: {string.Join(", ", jamFiles)}",
                    "No PilotDb action_set code represents Jam."
                }
                : new List<string>
                {
                    "PilotDb contains no Jam action_set code.",
                    "No reusable Jam dial control was found."
                }
        });

        results.Add(BuildAbsentResult(
            "Reload",
            codes,
            new[] { "Reload", "RL" }));

        var rotateCodes = new[] { "Rot", "Rot180" }
            .Where(code => codes.Contains(code))
            .ToList();

        var rotateUsage = rotateCodes
            .SelectMany(code =>
                repositoryUsage.TryGetValue(code, out var files)
                    ? files.Select(file => $"{code}: {file}")
                    : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        results.Add(new FirstEditionActionRuntimeDisposition
        {
            OfficialAction = "Rotate Arc",
            RuntimeCode = rotateCodes.Contains(
                "Rot",
                StringComparer.OrdinalIgnoreCase)
                ? "Rot"
                : null,
            AdditionalRuntimeCodes = rotateCodes
                .Where(code => !code.Equals(
                    "Rot",
                    StringComparison.OrdinalIgnoreCase))
                .ToList(),
            Disposition = rotateCodes.Count > 0
                ? "RuntimeMetadataCodeRequiresIntegration"
                : "RuntimeExtensionRequired",
            Evidence = new List<string>
            {
                rotateCodes.Count > 0
                    ? $"PilotDb codes found: {string.Join(", ", rotateCodes)}."
                    : "No Rotate Arc code was found in PilotDb.",
                branches.Any(branch =>
                    rotateCodes.Contains(
                        branch.RuntimeCode,
                        StringComparer.OrdinalIgnoreCase))
                    ? "A Rotate Arc actSet branch exists in the assigned dial."
                    : "The assigned dial actSet loop does not consume Rot/Rot180.",
                rotateUsage.Count > 0
                    ? $"Repository usage: {string.Join(" | ", rotateUsage.Take(12))}"
                    : "No additional Lua usage was identified by this scan."
            }
        });

        results.Add(BuildAbsentResult(
            "SLAM",
            codes,
            new[] { "SLAM", "Slam", "S" }));

        return results;
    }

    private static FirstEditionActionRuntimeDisposition BuildAbsentResult(
        string officialAction,
        IReadOnlySet<string> codes,
        IEnumerable<string> candidateCodes)
    {
        var found = candidateCodes
            .Where(code => codes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FirstEditionActionRuntimeDisposition
        {
            OfficialAction = officialAction,
            RuntimeCode = found.Count == 1 ? found[0] : null,
            AdditionalRuntimeCodes = found.Skip(1).ToList(),
            Disposition = found.Count > 0
                ? "RuntimeCodeRequiresVerification"
                : "RuntimeExtensionRequired",
            Evidence = found.Count > 0
                ? new List<string>
                {
                    $"Candidate PilotDb codes found: {string.Join(", ", found)}.",
                    "No direct assigned-dial actSet branch verified the meaning."
                }
                : new List<string>
                {
                    $"PilotDb contains no action_set code for {officialAction}.",
                    "The First Edition dial/UI will require a new explicit control or separate semantic handling."
                }
        };
    }

    private static Dictionary<string, List<string>> ScanRepositoryUsage(
        IReadOnlyList<string> luaFiles,
        IReadOnlyList<RuntimeActionCodeInventory> inventory)
    {
        var result = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in luaFiles)
        {
            var text = File.ReadAllText(file);

            foreach (var code in inventory.Select(item => item.RuntimeCode))
            {
                if (!ContainsQuotedCode(text, code))
                    continue;

                if (!result.TryGetValue(code, out var files))
                {
                    files = new List<string>();
                    result.Add(code, files);
                }

                files.Add(NormalisePath(file));
            }
        }

        return result;
    }

    private static bool ContainsQuotedCode(string text, string code) =>
        Regex.IsMatch(
            text,
            $@"['""]{Regex.Escape(code)}['""]",
            RegexOptions.CultureInvariant);

    private static string ResolveUnifiedRoot(string repositoryRoot)
    {
        var candidate = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified25");

        return Directory.Exists(candidate)
            ? candidate
            : repositoryRoot;
    }

    private static string FindPilotDb(string unifiedRoot)
    {
        var direct = Path.Combine(
            unifiedRoot,
            "TTS_xwing",
            "src",
            "Game",
            "Component",
            "Spawner",
            "PilotDb.lua");

        if (File.Exists(direct))
            return direct;

        return Directory
            .EnumerateFiles(
                unifiedRoot,
                "PilotDb.lua",
                SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? direct;
    }

    private static List<string> FindDialLuaFiles(string unifiedRoot) =>
        Directory
            .EnumerateFiles(
                unifiedRoot,
                "*.lua",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var normalised = NormalisePath(path);
                return Path.GetFileName(path)
                           .Contains(
                               "Dial",
                               StringComparison.OrdinalIgnoreCase)
                    || normalised.Contains(
                        "/Dial/",
                        StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
                "runtime-action-code-analysis")
            : Path.GetFullPath(option);
    }

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(
                    option,
                    StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static void WriteInventoryCsv(
        string path,
        IEnumerable<RuntimeActionCodeInventory> inventory)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "RuntimeCode,PilotCount,ExamplePilots");

        foreach (var item in inventory)
        {
            writer.WriteLine(string.Join(',',
                Csv(item.RuntimeCode),
                item.PilotCount,
                Csv(string.Join('|', item.ExamplePilots))));
        }
    }

    private static void WriteTargetCsv(
        string path,
        IEnumerable<FirstEditionActionRuntimeDisposition> targets)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "OfficialAction,RuntimeCode,AdditionalRuntimeCodes," +
            "Disposition,Evidence");

        foreach (var item in targets)
        {
            writer.WriteLine(string.Join(',',
                Csv(item.OfficialAction),
                Csv(item.RuntimeCode ?? string.Empty),
                Csv(string.Join('|', item.AdditionalRuntimeCodes)),
                Csv(item.Disposition),
                Csv(string.Join('|', item.Evidence))));
        }
    }

    private static void WriteMarkdown(
        string path,
        RuntimeActionCodeAnalysisManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 11F-2 – Unified Runtime Action-Code Contract");
        writer.WriteLine();
        writer.WriteLine(
            $"- Pilot records parsed: **{manifest.PilotRecordsParsed}**");
        writer.WriteLine(
            $"- Pilots with `action_set`: **{manifest.PilotRecordsWithActionSet}**");
        writer.WriteLine(
            $"- Unique runtime codes: **{manifest.UniqueRuntimeCodes}**");
        writer.WriteLine(
            $"- Dial Lua files scanned: **{manifest.DialLuaFilesScanned}**");
        writer.WriteLine();
        writer.WriteLine(
            "| First Edition action | Runtime code | Disposition |");
        writer.WriteLine("|---|---|---|");

        foreach (var target in manifest.TargetActions)
        {
            writer.WriteLine(
                $"| {Md(target.OfficialAction)} | " +
                $"{Md(target.RuntimeCode ?? string.Empty)} | " +
                $"{Md(target.Disposition)} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Important findings");
        writer.WriteLine();
        writer.WriteLine(
            "- `C` is Calculate in the Unified dial. It must not be reused for First Edition Coordinate.");
        writer.WriteLine(
            "- Jam is currently an independent token control (`JamBtn`/`jamTok`), not an `actSet` code.");
        writer.WriteLine(
            "- `Rot` and `Rot180` exist in PilotDb, but are not consumed by the assigned dial's `actSet` loop.");
        writer.WriteLine(
            "- Reload and SLAM have no reusable PilotDb `action_set` code in this runtime snapshot.");
        writer.WriteLine();
        writer.WriteLine(
            "Therefore the First Edition action bar must remain semantic data. " +
            "Only controls genuinely supported by the reused dial should be placed in `actSet`; " +
            "the remaining First Edition actions require explicit UI/runtime integration.");
    }

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
            throw new FileNotFoundException(
                $"{description} was not found.",
                path);
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  analyse-runtime-action-codes <first-edition-repository> " +
            "[--output <folder>]");
    }
}

public sealed class RuntimeActionCodeAnalysisManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string UnifiedRoot { get; init; } = string.Empty;
    public string PilotDbPath { get; init; } = string.Empty;
    public int PilotRecordsParsed { get; init; }
    public int PilotRecordsWithActionSet { get; init; }
    public int UniqueRuntimeCodes { get; init; }
    public int DialLuaFilesScanned { get; init; }
    public List<RuntimeActionCodeInventory> RuntimeCodes { get; init; } = new();
    public List<DialActionBranch> DialBranches { get; init; } = new();
    public List<FirstEditionActionRuntimeDisposition> TargetActions { get; init; } = new();
    public int ReusableTargetMappings { get; init; }
    public int RuntimeExtensionsRequired { get; init; }
}

public sealed class PilotActionSetRecord
{
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public List<string> ActionSet { get; init; } = new();
}

public sealed class RuntimeActionCodeInventory
{
    public string RuntimeCode { get; init; } = string.Empty;
    public int PilotCount { get; init; }
    public List<string> ExamplePilots { get; init; } = new();
}

public sealed class DialActionBranch
{
    public string RuntimeCode { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public List<string> UiControls { get; init; } = new();
    public string? Meaning { get; init; }
}

public sealed class FirstEditionActionRuntimeDisposition
{
    public string OfficialAction { get; init; } = string.Empty;
    public string? RuntimeCode { get; init; }
    public List<string> AdditionalRuntimeCodes { get; init; } = new();
    public string Disposition { get; init; } = string.Empty;
    public List<string> Evidence { get; init; } = new();
}
