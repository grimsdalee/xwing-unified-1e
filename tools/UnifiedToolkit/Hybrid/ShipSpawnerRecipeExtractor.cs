using System.Text.Json;
using System.Text.RegularExpressions;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Hybrid;

public sealed class ShipSpawnerRecipeAnalysis
{
    public string SourceSave { get; init; } = "";
    public string ModuleName { get; init; } = "Game.Component.Spawner.Spawner";
    public int ModuleStartLine { get; init; }
    public int ModuleEndLine { get; init; }
    public IReadOnlyList<ShipSpawnerFunctionBody> Functions { get; init; } = Array.Empty<ShipSpawnerFunctionBody>();
    public IReadOnlyList<ShipSpawnerCallEdge> CallGraph { get; init; } = Array.Empty<ShipSpawnerCallEdge>();
    public ShipBasePrototypeSelection BasePrototype { get; init; } = new();
    public ShipModelAttachmentRecipe ModelAttachment { get; init; } = new();
    public ShipConfigAttachmentRecipe ConfigAttachment { get; init; } = new();
    public IReadOnlyList<SpawnerIndirectObjectReference> IndirectObjectReferences { get; init; } = Array.Empty<SpawnerIndirectObjectReference>();
    public IReadOnlyList<FirstEditionShipConstructionRecipe> FirstEditionRecipes { get; init; } = Array.Empty<FirstEditionShipConstructionRecipe>();
    public ShipSpawnerRecipeSummary Summary { get; init; } = new();
}

public sealed class ShipSpawnerFunctionBody
{
    public string Name { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string Parameters { get; init; } = "";
    public string Body { get; init; } = "";
    public IReadOnlyList<string> Operations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Calls { get; init; } = Array.Empty<string>();
}

public sealed class ShipSpawnerCallEdge
{
    public string Caller { get; init; } = "";
    public string Callee { get; init; } = "";
    public int Line { get; init; }
}

public sealed class ShipBasePrototypeSelection
{
    public string GuidVariable { get; init; } = "CompositeBase_GUID";
    public string Guid { get; init; } = "";
    public bool Resolved { get; init; }
    public string ObjectName { get; init; } = "";
    public string ObjectNickname { get; init; } = "";
    public string ObjectPath { get; init; } = "";
    public string CloneExpression { get; init; } = "";
    public string BaseMeshExpression { get; init; } = "";
    public string BaseDiffuseExpression { get; init; } = "";
    public string ColliderExpression { get; init; } = "";
    public string ScaleExpression { get; init; } = "";
    public IReadOnlyDictionary<string, string> SpawnOffsetsBySourceSize { get; init; } = new Dictionary<string, string>();
}

public sealed class ShipModelAttachmentRecipe
{
    public string FunctionName { get; init; } = "pegAndShipSpawnFunction";
    public string PegObjectType { get; init; } = "";
    public string PegMeshExpression { get; init; } = "";
    public string PegDiffuseExpression { get; init; } = "";
    public string PegColliderExpression { get; init; } = "";
    public string PegScaleExpression { get; init; } = "";
    public string ShipObjectType { get; init; } = "";
    public string ShipMeshExpression { get; init; } = "";
    public string ShipDiffuseExpression { get; init; } = "";
    public string ShipColliderExpression { get; init; } = "";
    public string ShipOffsetExpression { get; init; } = "";
    public string ShipScaleExpression { get; init; } = "";
    public string AttachmentExpression { get; init; } = "";
}

public sealed class ShipConfigAttachmentRecipe
{
    public string IdentifierFunction { get; init; } = "shipIdCustomObjectForSize";
    public IReadOnlyDictionary<string, string> IdentifierMeshesBySourceSize { get; init; } = new Dictionary<string, string>();
    public string IdentifierScaleExpression { get; init; } = "";
    public string ConfigModelSourceExpression { get; init; } = "";
    public string ConfigTextureExpression { get; init; } = "";
    public string HiddenConfigScaleExpression { get; init; } = "";
    public bool DisablesAttachedColliders { get; init; }
    public bool SupportsConfigurationToken { get; init; }
}

public sealed class SpawnerIndirectObjectReference
{
    public string VariableName { get; init; } = "";
    public string Key { get; init; } = "";
    public string Guid { get; init; } = "";
    public bool Resolved { get; init; }
    public string ObjectName { get; init; } = "";
    public string ObjectNickname { get; init; } = "";
    public string ObjectPath { get; init; } = "";
    public IReadOnlyList<string> UsedByFunctions { get; init; } = Array.Empty<string>();
}

public sealed class FirstEditionShipConstructionRecipe
{
    public string DefinitionId { get; init; } = "";
    public FirstEditionBaseSize BaseSize { get; init; }
    public string Source25RecipeSize { get; init; } = "";
    public bool MediumPermitted { get; init; }
    public bool UsesCompositeBaseClone { get; init; }
    public string BaseMeshPathTemplate { get; init; } = "";
    public string PegMeshPathTemplate { get; init; } = "";
    public string IdentifierMesh { get; init; } = "";
    public string ModelSource { get; init; } = "HybridShipDefinition.Appearance";
    public string BaseSizeSource { get; init; } = "FirstEditionSemanticRepository";
    public IReadOnlyList<string> ConstructionSteps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationRules { get; init; } = Array.Empty<string>();
}

public sealed class ShipSpawnerRecipeSummary
{
    public int FocusedFunctionCount { get; init; }
    public int CallGraphEdgeCount { get; init; }
    public int IndirectReferenceCount { get; init; }
    public int ResolvedIndirectReferenceCount { get; init; }
    public bool CompositeBaseResolved { get; init; }
    public bool PegRecipeExtracted { get; init; }
    public bool ShipModelRecipeExtracted { get; init; }
    public bool ConfigRecipeExtracted { get; init; }
    public int FirstEditionRecipeCount { get; init; }
    public bool MediumRejected { get; init; }
}

public static class ShipSpawnerRecipeExtractor
{
    private static readonly string[] FocusFunctions =
    [
        "spawnCompositeShipBase", "shipIdCustomObjectForSize", "spawnShipIdentifiersAndConfig",
        "spawnPilotShipBundle", "spawnPilotBundle", "spawnPilotCardAndDial", "spawnPilotAccessories",
        "initializeSpawnerAccessoryContext", "takeSpawnerAccessorySource", "cloneSpawnerToken",
        "cloneSpawnerAccessory", "spawnSpawnerObstacles"
    ];

    public static ShipSpawnerRecipeAnalysis Analyse(string savePath)
    {
        var game = TtsSaveLoader.Load(savePath);
        var lua = Normalize(game.GlobalLua ?? "");
        var lines = lua.Split('\n');
        var module = FindModule(lines, "Game.Component.Spawner.Spawner");
        var functions = FocusFunctions.Select(name => ExtractFunction(lines, name, module.Start, module.End))
            .Where(x => x is not null).Cast<ShipSpawnerFunctionBody>().ToArray();
        var calls = BuildCallGraph(functions);
        var objects = Flatten(game.Objects).Where(x => !string.IsNullOrWhiteSpace(x.Guid))
            .GroupBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var variables = ParseGuidVariables(lines);
        var indirect = BuildIndirectReferences(variables, functions, objects);
        var basePrototype = BuildBasePrototype(functions, variables, objects);
        var modelRecipe = BuildModelRecipe(functions);
        var configRecipe = BuildConfigRecipe(functions);
        var recipes = BuildFirstEditionRecipes(basePrototype, modelRecipe, configRecipe);

        return new ShipSpawnerRecipeAnalysis
        {
            SourceSave = Path.GetFullPath(savePath),
            ModuleStartLine = module.Start + 1,
            ModuleEndLine = module.End,
            Functions = functions,
            CallGraph = calls,
            BasePrototype = basePrototype,
            ModelAttachment = modelRecipe,
            ConfigAttachment = configRecipe,
            IndirectObjectReferences = indirect,
            FirstEditionRecipes = recipes,
            Summary = new ShipSpawnerRecipeSummary
            {
                FocusedFunctionCount = functions.Length,
                CallGraphEdgeCount = calls.Count,
                IndirectReferenceCount = indirect.Count,
                ResolvedIndirectReferenceCount = indirect.Count(x => x.Resolved),
                CompositeBaseResolved = basePrototype.Resolved,
                PegRecipeExtracted = !string.IsNullOrWhiteSpace(modelRecipe.PegMeshExpression),
                ShipModelRecipeExtracted = !string.IsNullOrWhiteSpace(modelRecipe.ShipMeshExpression),
                ConfigRecipeExtracted = configRecipe.IdentifierMeshesBySourceSize.Count > 0,
                FirstEditionRecipeCount = recipes.Count,
                MediumRejected = recipes.All(x => !x.MediumPermitted)
            }
        };
    }

    private static (int Start, int End) FindModule(string[] lines, string moduleName)
    {
        var pattern = new Regex($"__bundle_register\\s*\\(\\s*['\"]{Regex.Escape(moduleName)}['\"]", RegexOptions.Compiled);
        var start = Array.FindIndex(lines, x => pattern.IsMatch(x));
        if (start < 0) throw new InvalidDataException($"Lua bundle module not found: {moduleName}");
        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
            if (lines[i].Contains("__bundle_register(", StringComparison.Ordinal)) { end = i; break; }
        return (start, end);
    }

    private static ShipSpawnerFunctionBody? ExtractFunction(string[] lines, string name, int moduleStart, int moduleEnd)
    {
        var declaration = new Regex($@"^\s*(?:local\s+)?function\s+{Regex.Escape(name)}\s*\((?<params>[^)]*)\)");
        var start = -1;
        Match? match = null;
        for (var i = moduleStart; i < moduleEnd; i++)
        {
            var current = declaration.Match(lines[i]);
            if (!current.Success) continue;
            start = i; match = current; break;
        }
        if (start < 0) return null;
        var end = moduleEnd;
        for (var i = start + 1; i < moduleEnd; i++)
        {
            if (Regex.IsMatch(lines[i], @"^\s*(?:local\s+)?function\s+[A-Za-z_][A-Za-z0-9_]*\s*\(")) { end = i; break; }
        }
        var body = string.Join('\n', lines[start..end]);
        var operations = new[] { "clone", "spawnObject", "takeObject", "addAttachment", "setCustomObject", "setTable", "setLuaScript", "waitCondition" }
            .Where(x => body.Contains(x, StringComparison.Ordinal)).ToArray();
        var calls = FocusFunctions.Where(x => x != name && Regex.IsMatch(body, $@"\b{Regex.Escape(x)}\s*\(")).ToArray();
        return new ShipSpawnerFunctionBody { Name = name, StartLine = start + 1, EndLine = end, Parameters = match!.Groups["params"].Value.Trim(), Body = body, Operations = operations, Calls = calls };
    }

    private static IReadOnlyList<ShipSpawnerCallEdge> BuildCallGraph(IReadOnlyList<ShipSpawnerFunctionBody> functions)
    {
        var result = new List<ShipSpawnerCallEdge>();
        foreach (var function in functions)
            foreach (var callee in function.Calls)
            {
                var offset = function.Body.IndexOf(callee + "(", StringComparison.Ordinal);
                var line = function.StartLine + (offset < 0 ? 0 : function.Body[..offset].Count(c => c == '\n'));
                result.Add(new ShipSpawnerCallEdge { Caller = function.Name, Callee = callee, Line = line });
            }
        return result;
    }

    private static Dictionary<string, string> ParseGuidVariables(string[] lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var simple = new Regex("^\\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*['\"](?<guid>[0-9a-fA-F]{6})['\"]");
        var item = new Regex("\\[['\"](?<key>[^'\"]+)['\"]\\]\\s*=\\s*['\"](?<guid>[0-9a-fA-F]{6})['\"]");
        var table = "";
        foreach (var line in lines)
        {
            var tableStart = Regex.Match(line, @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{");
            if (tableStart.Success) table = tableStart.Groups["name"].Value;
            var s = simple.Match(line);
            if (s.Success) result[s.Groups["name"].Value] = s.Groups["guid"].Value;
            var m = item.Match(line);
            if (m.Success && !string.IsNullOrWhiteSpace(table)) result[$"{table}.{m.Groups["key"].Value}"] = m.Groups["guid"].Value;
            if (!string.IsNullOrWhiteSpace(table) && line.TrimStart().StartsWith("}", StringComparison.Ordinal)) table = "";
        }
        return result;
    }

    private static IReadOnlyList<SpawnerIndirectObjectReference> BuildIndirectReferences(Dictionary<string, string> variables, IReadOnlyList<ShipSpawnerFunctionBody> functions, IReadOnlyDictionary<string, TtsObject> objects)
    {
        return variables.Where(x => x.Key.Contains("GUID", StringComparison.OrdinalIgnoreCase) || x.Key.StartsWith("bagGuids.", StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                objects.TryGetValue(x.Value, out var obj);
                var variable = x.Key.Contains('.') ? x.Key[..x.Key.IndexOf('.')] : x.Key;
                var key = x.Key.Contains('.') ? x.Key[(x.Key.IndexOf('.') + 1)..] : "";
                var uses = functions.Where(f => f.Body.Contains(variable, StringComparison.Ordinal) && (string.IsNullOrEmpty(key) || f.Body.Contains(key, StringComparison.Ordinal))).Select(f => f.Name).ToArray();
                return new SpawnerIndirectObjectReference { VariableName = variable, Key = key, Guid = x.Value, Resolved = obj is not null, ObjectName = obj?.Name ?? "", ObjectNickname = obj?.Nickname ?? "", ObjectPath = obj is null ? "" : BuildPath(obj), UsedByFunctions = uses };
            }).OrderBy(x => x.VariableName).ThenBy(x => x.Key).ToArray();
    }

    private static ShipBasePrototypeSelection BuildBasePrototype(IReadOnlyList<ShipSpawnerFunctionBody> functions, Dictionary<string, string> variables, IReadOnlyDictionary<string, TtsObject> objects)
    {
        var body = functions.First(x => x.Name == "spawnCompositeShipBase").Body;
        variables.TryGetValue("CompositeBase_GUID", out var guid);
        objects.TryGetValue(guid ?? "", out var obj);
        return new ShipBasePrototypeSelection
        {
            Guid = guid ?? "", Resolved = obj is not null, ObjectName = obj?.Name ?? "", ObjectNickname = obj?.Nickname ?? "", ObjectPath = obj is null ? "" : BuildPath(obj),
            CloneExpression = FindLine(body, "base_prototype.clone"), BaseMeshExpression = FindLine(body, "bases/\" .. size .. \"/base.obj"),
            BaseDiffuseExpression = FindLine(body, "local baseDiffuse"), ColliderExpression = FindLine(body, "ShipVerification.colliders[size]"), ScaleExpression = FindLine(body, "setScale(Dim.mm_ship_scale"),
            SpawnOffsetsBySourceSize = new Dictionary<string, string> { ["small"] = "LocalPos(spawnCard, { 0, 0, 10.2 })", ["medium"] = "LocalPos(spawnCard, { 0, 0, 10.2 })", ["large"] = "LocalPos(spawnCard, { 0, 0, 10.2 })", ["huge"] = "LocalPos(spawnCard, { 0, 0, 13.2 })" }
        };
    }

    private static ShipModelAttachmentRecipe BuildModelRecipe(IReadOnlyList<ShipSpawnerFunctionBody> functions)
    {
        var body = functions.First(x => x.Name == "spawnCompositeShipBase").Body;
        return new ShipModelAttachmentRecipe
        {
            PegObjectType = "Custom_Model", PegMeshExpression = FindLine(body, "bases/pegs/"), PegDiffuseExpression = "baseDiffuse", PegColliderExpression = FindLine(body, "minisculebox.obj"), PegScaleExpression = "Dim.mm_ship_scale",
            ShipObjectType = "Custom_Model", ShipMeshExpression = FindLine(body, "pilot.Data.mesh"), ShipDiffuseExpression = "resolved First Edition appearance texture", ShipColliderExpression = FindLine(body, "minisculebox.obj"),
            ShipOffsetExpression = FindLine(body, "local shipoffset"), ShipScaleExpression = "Dim.mm_ship_scale", AttachmentExpression = "ship.addAttachment(pegModel); ship.addAttachment(shipModel)"
        };
    }

    private static ShipConfigAttachmentRecipe BuildConfigRecipe(IReadOnlyList<ShipSpawnerFunctionBody> functions)
    {
        var id = functions.First(x => x.Name == "shipIdCustomObjectForSize").Body;
        var config = functions.First(x => x.Name == "spawnShipIdentifiersAndConfig").Body;
        var meshes = new Dictionary<string, string>();
        foreach (var size in new[] { "small", "medium", "large", "huge" })
        {
            var match = Regex.Match(id, $"if\\s+size\\s*==\\s*['\"]{size}['\"](?<body>.*?)(?=elseif|end)", RegexOptions.Singleline);
            var url = Regex.Match(match.Groups["body"].Value, "['\"](?<url>\\{verifycache\\}[^'\"]+\\.obj)['\"]");
            if (url.Success) meshes[size] = url.Groups["url"].Value;
        }
        return new ShipConfigAttachmentRecipe
        {
            IdentifierMeshesBySourceSize = meshes, IdentifierScaleExpression = "Dim.mm_ship_scale * 0.99", ConfigModelSourceExpression = "config.Model", ConfigTextureExpression = "resolved appearance texture",
            HiddenConfigScaleExpression = "vector(0.0001, 0.0001, 0.0001)", DisablesAttachedColliders = config.Contains("DisableAttachedColliders", StringComparison.Ordinal), SupportsConfigurationToken = config.Contains("Config.Token", StringComparison.Ordinal)
        };
    }

    private static IReadOnlyList<FirstEditionShipConstructionRecipe> BuildFirstEditionRecipes(ShipBasePrototypeSelection basePrototype, ShipModelAttachmentRecipe model, ShipConfigAttachmentRecipe config)
    {
        var mappings = new[] { (FirstEditionBaseSize.Small, "small"), (FirstEditionBaseSize.Large, "large"), (FirstEditionBaseSize.Epic, "huge") };
        return mappings.Select(x => new FirstEditionShipConstructionRecipe
        {
            DefinitionId = $"first-edition-{x.Item1.ToString().ToLowerInvariant()}-ship-construction", BaseSize = x.Item1, Source25RecipeSize = x.Item2, MediumPermitted = false, UsesCompositeBaseClone = basePrototype.Resolved,
            BaseMeshPathTemplate = "spawnPrefix + bases/{sourceRecipeSize}/base.obj?1", PegMeshPathTemplate = "spawnPrefix + bases/pegs/{pegType}.obj", IdentifierMesh = config.IdentifierMeshesBySourceSize.GetValueOrDefault(x.Item2, ""),
            ConstructionSteps = ["Clone CompositeBase prototype", "Apply authoritative First Edition base mesh and faction/firing-arc texture", "Apply semantic Data and UiData", "Spawn and attach peg", "Spawn and attach selected 1E appearance model", "Spawn and attach colour identifier", "Attach optional configuration models and tokens", "Unlock and initialize ship behaviour"],
            ValidationRules = ["Base size comes only from the First Edition semantic repository.", "Medium is never a generated destination.", "A 2.5 Medium model is remounted using the Small or Large First Edition recipe.", "Epic maps to the source Huge construction mechanics but retains First Edition terminology.", "Model scale and vertical offset require prototype validation in Tabletop Simulator."]
        }).ToArray();
    }

    private static string FindLine(string body, string contains) => body.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Contains(contains, StringComparison.Ordinal)) ?? "";
    private static string Normalize(string raw)
    {
        if (raw.Contains("\\n", StringComparison.Ordinal) && raw.Count(c => c == '\n') < 10)
        {
            try { return JsonSerializer.Deserialize<string>("\"" + raw + "\"")!.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'); }
            catch (JsonException) { }
        }
        return raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
    private static IEnumerable<TtsObject> Flatten(IEnumerable<TtsObject> roots) { foreach (var root in roots) { yield return root; foreach (var child in Flatten(root.Children)) yield return child; } }
    private static string BuildPath(TtsObject obj) { var parts = new Stack<string>(); for (var current = obj; current is not null; current = current.Parent) parts.Push(string.IsNullOrWhiteSpace(current.Nickname) ? current.Name : current.Nickname); return string.Join(" / ", parts); }
}
