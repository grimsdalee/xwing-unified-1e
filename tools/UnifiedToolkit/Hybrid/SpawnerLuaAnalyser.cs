using System.Text.Json;
using System.Text.RegularExpressions;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Hybrid;

public sealed class SpawnerLuaAnalysis
{
    public string SourceSave { get; init; } = "";
    public int GlobalLuaCharacters { get; init; }
    public SpawnerLuaNormalization Normalization { get; init; } = new();
    public IReadOnlyList<SpawnerBundleModule> BundleModules { get; init; } = Array.Empty<SpawnerBundleModule>();
    public IReadOnlyList<SpawnerEntryPoint> EntryPoints { get; init; } = Array.Empty<SpawnerEntryPoint>();
    public IReadOnlyList<SpawnerFunctionRecord> Functions { get; init; } = Array.Empty<SpawnerFunctionRecord>();
    public IReadOnlyList<SpawnerCallSite> CallSites { get; init; } = Array.Empty<SpawnerCallSite>();
    public IReadOnlyList<SpawnerObjectReference> ObjectReferences { get; init; } = Array.Empty<SpawnerObjectReference>();
    public IReadOnlyList<ShipConstructionStage> ConstructionPipeline { get; init; } = Array.Empty<ShipConstructionStage>();
    public IReadOnlyList<FirstEditionSpawnerDefinition> FirstEditionDefinitions { get; init; } = Array.Empty<FirstEditionSpawnerDefinition>();
    public SpawnerLuaSummary Summary { get; init; } = new();
}

public sealed class SpawnerLuaNormalization
{
    public int RawCharacterCount { get; init; }
    public int NormalizedCharacterCount { get; init; }
    public int RawLineCount { get; init; }
    public int NormalizedLineCount { get; init; }
    public string EncodingDetected { get; init; } = "PlainText";
    public int DecodePassesApplied { get; init; }
    public IReadOnlyList<string> Transformations { get; init; } = Array.Empty<string>();
    public int BundleModuleCount { get; init; }
    public bool ValidationPassed { get; init; }
}

public sealed class SpawnerBundleModule
{
    public string Name { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public int StartOffset { get; init; }
}

public sealed class SpawnerLuaSummary
{
    public int FunctionCount { get; init; }
    public int BundleModuleCount { get; init; }
    public int EntryPointCount { get; init; }
    public int SpawnJsonCallCount { get; init; }
    public int SpawnObjectCallCount { get; init; }
    public int TakeObjectCallCount { get; init; }
    public int CloneCallCount { get; init; }
    public int ReferencedGuidCount { get; init; }
    public int ResolvedGuidCount { get; init; }
    public bool MediumRejected { get; init; }
}

public sealed class SpawnerEntryPoint
{
    public string ModuleName { get; init; } = "";
    public string FunctionName { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public IReadOnlyList<string> Operations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SizeTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed class SpawnerFunctionRecord
{
    public string ModuleName { get; init; } = "";
    public string Name { get; init; } = "";
    public string DeclarationKind { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public int StartOffset { get; init; }
    public string Parameters { get; init; } = "";
    public IReadOnlyList<string> DirectOperations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CalledFunctions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SizeTerms { get; init; } = Array.Empty<string>();
    public string Preview { get; init; } = "";
}

public sealed class SpawnerCallSite
{
    public string Operation { get; init; } = "";
    public string ModuleName { get; init; } = "";
    public string FunctionName { get; init; } = "";
    public int Line { get; init; }
    public int CharacterOffset { get; init; }
    public string Context { get; init; } = "";
}

public sealed class SpawnerObjectReference
{
    public string Guid { get; init; } = "";
    public int Line { get; init; }
    public int CharacterOffset { get; init; }
    public string ModuleName { get; init; } = "";
    public string FunctionName { get; init; } = "";
    public bool Resolved { get; init; }
    public string ObjectName { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string ObjectPath { get; init; } = "";
    public string Context { get; init; } = "";
}

public sealed class ShipConstructionStage
{
    public int Order { get; init; }
    public string Stage { get; init; } = "";
    public string Status { get; init; } = "";
    public IReadOnlyList<string> EvidenceFunctions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed class FirstEditionSpawnerDefinition
{
    public string DefinitionId { get; init; } = "";
    public FirstEditionBaseSize BaseSize { get; init; }
    public int PegCount { get; init; }
    public bool SupportsEpicMovementTool { get; init; }
    public bool MediumPermitted { get; init; }
    public string ConstructionMode { get; init; } = "GeneratedJson";
    public IReadOnlyList<string> RequiredStages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceEvidenceFunctions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationRules { get; init; } = Array.Empty<string>();
}

public static class SpawnerLuaAnalyser
{
    private static readonly (string Operation, Regex Pattern)[] OperationPatterns =
    [
        ("spawnObjectJSON", new Regex(@"\bspawnObjectJSON\s*\(", RegexOptions.Compiled)),
        ("spawnObject", new Regex(@"\bspawnObject\s*\(", RegexOptions.Compiled)),
        ("takeObject", new Regex(@"(?:\.|\b)takeObject\s*\(", RegexOptions.Compiled)),
        ("clone", new Regex(@"(?:\.|\b)clone\s*\(", RegexOptions.Compiled))
    ];

    private static readonly Regex NamedFunctionRegex = new(
        @"^\s*(?:local\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_\.:]*)\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex AssignedFunctionRegex = new(
        "^\\s*(?:local\\s+)?(?<name>[A-Za-z_][A-Za-z0-9_\\.:]*(?:\\[['\"][^'\"]+['\"]\\])?)\\s*=\\s*function\\s*\\((?<params>[^)]*)\\)",
        RegexOptions.Compiled);

    private static readonly Regex BundleRegex = new(
        "__bundle_register\\s*\\(\\s*['\"](?<name>[^'\"]+)['\"]\\s*,\\s*function\\s*\\((?<params>[^)]*)\\)",
        RegexOptions.Compiled);

    private static readonly Regex GuidRegex = new("getObjectFromGUID\\s*\\(\\s*['\"](?<guid>[0-9a-fA-F]{6})['\"]\\s*\\)", RegexOptions.Compiled);
    private static readonly Regex CallRegex = new(@"\b(?<name>[A-Za-z_][A-Za-z0-9_\.:]*)\s*\(", RegexOptions.Compiled);

    public static SpawnerLuaAnalysis Analyse(string savePath)
    {
        var game = TtsSaveLoader.Load(savePath);
        var rawLua = game.GlobalLua ?? "";
        if (string.IsNullOrWhiteSpace(rawLua))
            throw new InvalidDataException("Unified save does not contain Global Lua.");

        var normalized = NormalizeLua(rawLua);
        var lua = normalized.Text;
        var lines = lua.Split('\n');
        var lineOffsets = BuildLineOffsets(lines);
        var modules = ParseBundleModules(lines, lineOffsets);

        if (lua.Length > 100_000 && lines.Length <= 1)
            throw new InvalidDataException("Global Lua normalization failed: a large script still contains one or fewer lines.");

        var functions = ParseFunctions(lines, lineOffsets, modules);
        var callSites = ParseCallSites(lines, lineOffsets, functions, modules);
        var objectReferences = ParseObjectReferences(lines, lineOffsets, functions, modules, game);
        var entries = BuildEntryPoints(functions, callSites);
        var pipeline = BuildPipeline(functions);
        var definitions = BuildFirstEditionDefinitions(entries, pipeline);

        var normalization = new SpawnerLuaNormalization
        {
            RawCharacterCount = rawLua.Length,
            NormalizedCharacterCount = lua.Length,
            RawLineCount = CountLines(rawLua),
            NormalizedLineCount = lines.Length,
            EncodingDetected = normalized.EncodingDetected,
            DecodePassesApplied = normalized.DecodePasses,
            Transformations = normalized.Transformations,
            BundleModuleCount = modules.Count,
            ValidationPassed = lua.Length <= 100_000 || lines.Length > 1
        };

        return new SpawnerLuaAnalysis
        {
            SourceSave = Path.GetFullPath(savePath),
            GlobalLuaCharacters = lua.Length,
            Normalization = normalization,
            BundleModules = modules,
            Functions = functions,
            CallSites = callSites,
            ObjectReferences = objectReferences,
            EntryPoints = entries,
            ConstructionPipeline = pipeline,
            FirstEditionDefinitions = definitions,
            Summary = new SpawnerLuaSummary
            {
                FunctionCount = functions.Count,
                BundleModuleCount = modules.Count,
                EntryPointCount = entries.Count,
                SpawnJsonCallCount = callSites.Count(x => x.Operation == "spawnObjectJSON"),
                SpawnObjectCallCount = callSites.Count(x => x.Operation == "spawnObject"),
                TakeObjectCallCount = callSites.Count(x => x.Operation == "takeObject"),
                CloneCallCount = callSites.Count(x => x.Operation == "clone"),
                ReferencedGuidCount = objectReferences.Select(x => x.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ResolvedGuidCount = objectReferences.Where(x => x.Resolved).Select(x => x.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                MediumRejected = definitions.All(x => !x.MediumPermitted)
            }
        };
    }

    private static (string Text, string EncodingDetected, int DecodePasses, IReadOnlyList<string> Transformations) NormalizeLua(string raw)
    {
        var text = raw;
        var transformations = new List<string>();
        var passes = 0;
        var detected = "PlainText";

        for (var pass = 0; pass < 2; pass++)
        {
            var rawLines = CountLines(text);
            var looksEscaped = text.Contains("\\n", StringComparison.Ordinal) ||
                               text.Contains("\\r", StringComparison.Ordinal) ||
                               text.Contains("\\t", StringComparison.Ordinal) ||
                               Regex.IsMatch(text, @"\\u[0-9a-fA-F]{4}");
            if (!looksEscaped || (rawLines > 100 && pass > 0)) break;

            try
            {
                var decoded = JsonSerializer.Deserialize<string>("\"" + text + "\"");
                if (string.IsNullOrEmpty(decoded) || decoded == text) break;
                text = decoded;
                passes++;
                detected = passes == 1 ? "JsonEscapedText" : "DoubleJsonEscapedText";
                transformations.Add($"JSON string escape decoding pass {passes}");
            }
            catch (JsonException)
            {
                break;
            }
        }

        var normalizedLineEndings = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (!ReferenceEquals(normalizedLineEndings, text) && normalizedLineEndings != text)
            transformations.Add("Normalized line endings to LF");
        text = normalizedLineEndings;

        return (text, detected, passes, transformations);
    }

    private static IReadOnlyList<SpawnerBundleModule> ParseBundleModules(string[] lines, int[] lineOffsets)
    {
        var starts = new List<(int Index, string Name)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = BundleRegex.Match(lines[i]);
            if (match.Success) starts.Add((i, match.Groups["name"].Value));
        }

        var result = new List<SpawnerBundleModule>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : lines.Length;
            result.Add(new SpawnerBundleModule
            {
                Name = starts[i].Name,
                StartLine = starts[i].Index + 1,
                EndLine = end,
                StartOffset = lineOffsets[starts[i].Index]
            });
        }
        return result;
    }

    private static IReadOnlyList<SpawnerFunctionRecord> ParseFunctions(string[] lines, int[] lineOffsets, IReadOnlyList<SpawnerBundleModule> modules)
    {
        var starts = new List<(int Index, string Name, string Parameters, string Kind)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var bundle = BundleRegex.Match(lines[i]);
            if (bundle.Success)
            {
                starts.Add((i, $"__bundle_register:{bundle.Groups["name"].Value}", bundle.Groups["params"].Value, "BundleModule"));
                continue;
            }

            var named = NamedFunctionRegex.Match(lines[i]);
            if (named.Success)
            {
                starts.Add((i, named.Groups["name"].Value, named.Groups["params"].Value, "NamedFunction"));
                continue;
            }

            var assigned = AssignedFunctionRegex.Match(lines[i]);
            if (assigned.Success)
                starts.Add((i, assigned.Groups["name"].Value, assigned.Groups["params"].Value, "AssignedFunction"));
        }

        var records = new List<SpawnerFunctionRecord>();
        for (var index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            var endExclusive = index + 1 < starts.Count ? starts[index + 1].Index : lines.Length;
            var body = string.Join('\n', lines[start.Index..endExclusive]);
            var operations = OperationPatterns.Where(x => x.Pattern.IsMatch(body)).Select(x => x.Operation).ToArray();
            var sizeTerms = FindSizeTerms(body);
            var calls = CallRegex.Matches(body).Select(x => x.Groups["name"].Value)
                .Where(x => !x.Equals(start.Name, StringComparison.OrdinalIgnoreCase))
                .Where(x => !IsLanguageOrOperation(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(150).ToArray();
            if (operations.Length == 0 && sizeTerms.Length == 0 && !ContainsConstructionEvidence(body) && start.Kind != "BundleModule") continue;

            records.Add(new SpawnerFunctionRecord
            {
                ModuleName = ResolveModule(modules, start.Index + 1),
                Name = start.Name,
                DeclarationKind = start.Kind,
                StartLine = start.Index + 1,
                EndLine = endExclusive,
                StartOffset = lineOffsets[start.Index],
                Parameters = start.Parameters.Trim(),
                DirectOperations = operations,
                CalledFunctions = calls,
                SizeTerms = sizeTerms,
                Preview = CompactPreview(body)
            });
        }
        return records.OrderBy(x => x.StartLine).ToArray();
    }

    private static IReadOnlyList<SpawnerCallSite> ParseCallSites(string[] lines, int[] lineOffsets, IReadOnlyList<SpawnerFunctionRecord> functions, IReadOnlyList<SpawnerBundleModule> modules)
    {
        var result = new List<SpawnerCallSite>();
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (operation, pattern) in OperationPatterns)
            {
                foreach (Match match in pattern.Matches(lines[i]))
                {
                    result.Add(new SpawnerCallSite
                    {
                        Operation = operation,
                        ModuleName = ResolveModule(modules, i + 1),
                        FunctionName = ResolveFunction(functions, i + 1),
                        Line = i + 1,
                        CharacterOffset = lineOffsets[i] + match.Index,
                        Context = Context(lines, i, 2)
                    });
                }
            }
        }
        return result.OrderBy(x => x.CharacterOffset).ToArray();
    }

    private static IReadOnlyList<SpawnerObjectReference> ParseObjectReferences(string[] lines, int[] lineOffsets, IReadOnlyList<SpawnerFunctionRecord> functions, IReadOnlyList<SpawnerBundleModule> modules, TtsGame game)
    {
        var objects = Flatten(game.Objects).Where(x => !string.IsNullOrWhiteSpace(x.Guid)).GroupBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<SpawnerObjectReference>();
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in GuidRegex.Matches(lines[i]))
            {
                var guid = match.Groups["guid"].Value;
                objects.TryGetValue(guid, out var obj);
                result.Add(new SpawnerObjectReference
                {
                    Guid = guid,
                    Line = i + 1,
                    CharacterOffset = lineOffsets[i] + match.Index,
                    ModuleName = ResolveModule(modules, i + 1),
                    FunctionName = ResolveFunction(functions, i + 1),
                    Resolved = obj is not null,
                    ObjectName = obj?.Name ?? "",
                    Nickname = obj?.Nickname ?? "",
                    ObjectPath = obj is null ? "" : BuildPath(obj),
                    Context = Context(lines, i, 1)
                });
            }
        }
        return result.OrderBy(x => x.CharacterOffset).ToArray();
    }

    private static IReadOnlyList<SpawnerEntryPoint> BuildEntryPoints(IReadOnlyList<SpawnerFunctionRecord> functions, IReadOnlyList<SpawnerCallSite> callSites) =>
        functions.Where(x => x.DirectOperations.Count > 0 || x.CalledFunctions.Any(ContainsSpawnerWord))
            .Select(x => new SpawnerEntryPoint
            {
                ModuleName = x.ModuleName,
                FunctionName = x.Name,
                StartLine = x.StartLine,
                EndLine = x.EndLine,
                Operations = x.DirectOperations,
                SizeTerms = x.SizeTerms,
                Evidence = callSites.Where(c => c.FunctionName == x.Name && c.ModuleName == x.ModuleName).Select(c => $"Line {c.Line}: {c.Operation}").ToArray()
            }).OrderBy(x => x.StartLine).ToArray();

    private static IReadOnlyList<ShipConstructionStage> BuildPipeline(IReadOnlyList<SpawnerFunctionRecord> functions)
    {
        var stages = new[]
        {
            ("Select semantic ship and First Edition base size", new[] { "ship", "size", "base" }),
            ("Construct base JSON", new[] { "baseobject", "base", "json.encode", "decker" }),
            ("Construct peg or model attachment", new[] { "peg", "attach", "joint", "model" }),
            ("Apply transforms and snap points", new[] { "snap", "position", "rotation", "scale" }),
            ("Apply Lua, XML and object state", new[] { "luascript", "xmlui", "scriptstate", "setcommon" }),
            ("Spawn generated JSON", new[] { "spawnobjectjson", ".spawn" }),
            ("Resolve bag, clone or object dependencies", new[] { "takeobject", "clone", "getobjectfromguid" })
        };

        return stages.Select((stage, index) =>
        {
            var evidence = functions.Where(f => stage.Item2.Any(term => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || f.Preview.Contains(term, StringComparison.OrdinalIgnoreCase) || f.CalledFunctions.Any(c => c.Contains(term, StringComparison.OrdinalIgnoreCase)))).Take(30).ToArray();
            return new ShipConstructionStage
            {
                Order = index + 1,
                Stage = stage.Item1,
                Status = evidence.Length > 0 ? "EvidenceFound" : "RequiresManualTrace",
                EvidenceFunctions = evidence.Select(x => $"{x.ModuleName}::{x.Name}").Distinct().ToArray(),
                Evidence = evidence.Select(x => $"{x.ModuleName}::{x.Name} (lines {x.StartLine}-{x.EndLine})").ToArray()
            };
        }).ToArray();
    }

    private static IReadOnlyList<FirstEditionSpawnerDefinition> BuildFirstEditionDefinitions(IReadOnlyList<SpawnerEntryPoint> entries, IReadOnlyList<ShipConstructionStage> pipeline)
    {
        var evidence = entries.Where(x => x.Operations.Contains("spawnObjectJSON")).Select(x => $"{x.ModuleName}::{x.FunctionName}").Distinct().ToArray();
        return FirstEditionBaseDefinitionCatalogue.Definitions.Select(x => new FirstEditionSpawnerDefinition
        {
            DefinitionId = $"first-edition-{x.Size.ToString().ToLowerInvariant()}-spawner",
            BaseSize = x.Size,
            PegCount = x.PegCount,
            SupportsEpicMovementTool = x.SupportsEpicMovementTool,
            MediumPermitted = false,
            RequiredStages = pipeline.Select(p => p.Stage).ToArray(),
            SourceEvidenceFunctions = evidence,
            ValidationRules =
            [
                "First Edition semantic base size is authoritative.",
                "Only Small, Large and Epic are permitted.",
                "Medium source size may be retained only as provenance.",
                "Generated object JSON must not reference a Medium base definition.",
                "Appearance model scale and height must be validated against the selected First Edition base."
            ]
        }).ToArray();
    }

    private static string[] FindSizeTerms(string text) => new[] { "small", "medium", "large", "huge", "epic" }.Where(x => Regex.IsMatch(text, $@"\b{x}\b", RegexOptions.IgnoreCase)).ToArray();
    private static bool ContainsConstructionEvidence(string body) => Regex.IsMatch(body, @"\b(BaseObject|SetCommonOptions|JSON\.encode|SnapPoints|LuaScriptState|CustomMesh|Custom_Model|ChildObjects)\b", RegexOptions.IgnoreCase);
    private static bool ContainsSpawnerWord(string value) => value.Contains("spawn", StringComparison.OrdinalIgnoreCase) || value.Contains("decker", StringComparison.OrdinalIgnoreCase) || value.Contains("ship", StringComparison.OrdinalIgnoreCase);
    private static bool IsLanguageOrOperation(string value) => value is "if" or "for" or "while" or "pairs" or "ipairs" or "type" or "next" or "setmetatable" or "tostring" or "tonumber" or "error" or "assert" or "spawnObjectJSON" or "spawnObject" or "takeObject" or "clone" or "function";
    private static string ResolveFunction(IReadOnlyList<SpawnerFunctionRecord> functions, int line) => functions.LastOrDefault(x => x.StartLine <= line && x.EndLine >= line)?.Name ?? "<global>";
    private static string ResolveModule(IReadOnlyList<SpawnerBundleModule> modules, int line) => modules.LastOrDefault(x => x.StartLine <= line && x.EndLine >= line)?.Name ?? "<global>";
    private static string Context(string[] lines, int index, int radius) => CompactPreview(string.Join(' ', lines[Math.Max(0, index - radius)..Math.Min(lines.Length, index + radius + 1)]));
    private static int CountLines(string text) => string.IsNullOrEmpty(text) ? 0 : 1 + text.Count(c => c == '\n');

    private static int[] BuildLineOffsets(string[] lines)
    {
        var offsets = new int[lines.Length];
        var current = 0;
        for (var i = 0; i < lines.Length; i++) { offsets[i] = current; current += lines[i].Length + 1; }
        return offsets;
    }

    private static string CompactPreview(string value)
    {
        var compact = Regex.Replace(value, @"\s+", " ").Trim();
        return compact.Length <= 500 ? compact : compact[..500] + "…";
    }

    private static IEnumerable<TtsObject> Flatten(IEnumerable<TtsObject> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children)) yield return child;
        }
    }

    private static string BuildPath(TtsObject obj)
    {
        var parts = new Stack<string>();
        for (var current = obj; current is not null; current = current.Parent)
            parts.Push(string.IsNullOrWhiteSpace(current.Nickname) ? current.Name : current.Nickname);
        return string.Join(" / ", parts);
    }
}
