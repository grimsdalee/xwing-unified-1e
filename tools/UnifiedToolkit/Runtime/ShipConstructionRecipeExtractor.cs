using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Runtime;

public static class ShipConstructionRecipeExtractor
{
    private static readonly string[] DefaultFunctions =
    {
        "spawnPilotCardAndDial",
        "spawnCompositeShipBase",
        "pegAndShipSpawnFunction",
        "shipIdCustomObjectForSize",
        "spawnShipIdentifiersAndConfig",
        "spawnPilotShipBundle"
    };

    private static readonly HashSet<string> LuaKeywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for",
        "function", "goto", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while"
    };

    private static readonly Regex CallRegex =
        new(@"\b([A-Za-z_][A-Za-z0-9_\.:]*)\s*\(", RegexOptions.Compiled);

    private static readonly Regex IdentifierRegex =
        new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);

    private static readonly Regex GuidAssignmentRegex =
        new("""(?m)^\s*(?:local\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*['\"]([0-9A-Fa-f]{6})['\"]\s*,?\s*$""",
            RegexOptions.Compiled);

    private static readonly Regex GuidTableRegex =
        new("""(?ms)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{(.*?)^\s*\}""",
            RegexOptions.Compiled);

    private static readonly Regex GuidTableEntryRegex =
        new("""(?:\[\s*['\"]([^'\"]+)['\"]\s*\]|([A-Za-z_][A-Za-z0-9_]*))\s*=\s*['\"]([0-9A-Fa-f]{6})['\"]""",
            RegexOptions.Compiled);

    public static IReadOnlyList<string> GetDefaultFunctions() => DefaultFunctions;

    public static ShipConstructionRecipeReport Extract(
        string savePath,
        string outputFolder,
        string? runtimeReportPath = null,
        IReadOnlyList<string>? requestedFunctions = null)
    {
        var root = JsonNode.Parse(File.ReadAllText(savePath))?.AsObject()
            ?? throw new InvalidOperationException("The TTS save could not be parsed as a JSON object.");

        var globalLua = GetString(root, "LuaScript");
        var module = ExtractBundleModule(globalLua, "Game.Component.Spawner.Spawner");
        if (string.IsNullOrWhiteSpace(module))
            throw new InvalidOperationException("The Game.Component.Spawner.Spawner module was not found in Global Lua.");

        var functions = requestedFunctions is { Count: > 0 }
            ? requestedFunctions
            : DefaultFunctions;

        var report = new ShipConstructionRecipeReport
        {
            SourceSave = Path.GetFullPath(savePath),
            RequestedFunctions = functions.ToList()
        };

        Directory.CreateDirectory(outputFolder);
        var sourceFolder = Path.Combine(outputFolder, "function-source");
        Directory.CreateDirectory(sourceFolder);

        var globalGuidSymbols = ReadGuidSymbols(globalLua);
        var runtimeDependencies = LoadRuntimeDependencies(runtimeReportPath);

        foreach (var functionName in functions)
        {
            var recipe = ExtractFunctionRecipe(module, functionName, sourceFolder, globalGuidSymbols);
            report.Functions.Add(recipe);
        }

        var dependencyMap = new Dictionary<string, ShipConstructionDependency>(StringComparer.Ordinal);
        foreach (var recipe in report.Functions.Where(x => x.Found))
        {
            foreach (var symbol in recipe.GuidSymbols)
            {
                if (!globalGuidSymbols.TryGetValue(symbol, out var guid))
                    continue;

                if (!dependencyMap.TryGetValue(symbol, out var dependency))
                {
                    dependency = new ShipConstructionDependency
                    {
                        Symbol = symbol,
                        Guid = guid
                    };

                    if (runtimeDependencies.TryGetValue(symbol, out var runtime))
                    {
                        dependency.Resolved = runtime.Resolved;
                        dependency.ObjectName = runtime.ObjectName;
                        dependency.ObjectNickname = runtime.ObjectNickname;
                        dependency.ObjectPath = runtime.ObjectPath;
                        dependency.Assets = runtime.Assets.ToList();
                    }

                    dependencyMap[symbol] = dependency;
                }

                if (!dependency.UsedByFunctions.Contains(recipe.Name, StringComparer.Ordinal))
                    dependency.UsedByFunctions.Add(recipe.Name);
            }
        }

        report.Dependencies = dependencyMap.Values
            .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var foundCount = report.Functions.Count(x => x.Found);
        report.Findings.Add($"Located {foundCount} of {report.Functions.Count} requested construction functions.");
        report.Findings.Add($"Extracted {report.Functions.Sum(x => x.Calls.Count)} distinct function-call references across the requested functions.");
        report.Findings.Add($"Identified {report.Dependencies.Count} GUID-backed runtime dependencies used directly by the requested functions.");
        report.Findings.Add("Function bodies are emitted as individual Lua files for human review.");
        report.Findings.Add("This command does not generate, spawn, or modify any Tabletop Simulator objects.");

        return report;
    }

    private static ShipConstructionFunctionRecipe ExtractFunctionRecipe(
        string module,
        string functionName,
        string sourceFolder,
        IReadOnlyDictionary<string, string> globalGuidSymbols)
    {
        var recipe = new ShipConstructionFunctionRecipe { Name = functionName };
        var span = FindFunctionSpan(module, functionName);
        if (span is null)
            return recipe;

        var source = module.Substring(span.Value.Start, span.Value.Length);
        var sourceFileName = SanitizeFileName(functionName) + ".lua";
        File.WriteAllText(Path.Combine(sourceFolder, sourceFileName), source);

        recipe.Found = true;
        recipe.CharacterOffset = span.Value.Start;
        recipe.CharacterLength = span.Value.Length;
        recipe.SourceFile = Path.Combine("function-source", sourceFileName).Replace('\\', '/');

        var masked = MaskCommentsAndStrings(source);
        recipe.Calls = CallRegex.Matches(masked)
            .Select(x => x.Groups[1].Value)
            .Where(x => !LuaKeywords.Contains(x))
            .Where(x => !x.Equals(functionName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        recipe.ReferencedSymbols = IdentifierRegex.Matches(masked)
            .Select(x => x.Value)
            .Where(x => !LuaKeywords.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        recipe.StringLiterals = ExtractStringLiterals(source)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        recipe.UrlLiterals = recipe.StringLiterals
            .Where(IsAssetLikeString)
            .ToList();

        recipe.GuidSymbols = globalGuidSymbols.Keys
            .Where(symbol => SymbolIsReferenced(source, symbol))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return recipe;
    }

    private static (int Start, int Length)? FindFunctionSpan(string lua, string functionName)
    {
        var patterns = new[]
        {
            new Regex(@"\bfunction\s+" + Regex.Escape(functionName) + @"\s*\(", RegexOptions.Compiled),
            new Regex(@"\b" + Regex.Escape(functionName) + @"\s*=\s*function\s*\(", RegexOptions.Compiled)
        };

        Match? startMatch = null;
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(lua);
            if (match.Success && (startMatch is null || match.Index < startMatch.Index))
                startMatch = match;
        }

        if (startMatch is null)
            return null;

        var masked = MaskCommentsAndStrings(lua);
        var tokenRegex = new Regex(@"\b(function|if|for|while|repeat|end|until)\b", RegexOptions.Compiled);
        var depth = 0;
        var started = false;

        foreach (Match token in tokenRegex.Matches(masked, startMatch.Index))
        {
            switch (token.Value)
            {
                case "function":
                case "if":
                case "for":
                case "while":
                case "repeat":
                    depth++;
                    started = true;
                    break;
                case "end":
                case "until":
                    if (!started)
                        continue;
                    depth--;
                    if (depth == 0)
                    {
                        var end = token.Index + token.Length;
                        return (startMatch.Index, end - startMatch.Index);
                    }
                    break;
            }
        }

        return null;
    }

    private static string MaskCommentsAndStrings(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;

        while (i < chars.Length)
        {
            if (chars[i] == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
            {
                if (i + 3 < chars.Length && chars[i + 2] == '[' && chars[i + 3] == '[')
                {
                    var end = text.IndexOf("]]", i + 4, StringComparison.Ordinal);
                    var stop = end < 0 ? chars.Length : end + 2;
                    MaskRange(chars, i, stop);
                    i = stop;
                }
                else
                {
                    var end = text.IndexOf('\n', i + 2);
                    var stop = end < 0 ? chars.Length : end;
                    MaskRange(chars, i, stop);
                    i = stop;
                }
                continue;
            }

            if (chars[i] is '\'' or '"')
            {
                var quote = chars[i];
                var start = i++;
                var escaped = false;
                while (i < chars.Length)
                {
                    if (!escaped && chars[i] == quote)
                    {
                        i++;
                        break;
                    }
                    escaped = !escaped && chars[i] == '\\';
                    if (chars[i] != '\\')
                        escaped = false;
                    i++;
                }
                MaskRange(chars, start, i);
                continue;
            }

            i++;
        }

        return new string(chars);
    }

    private static void MaskRange(char[] chars, int start, int end)
    {
        for (var i = start; i < end && i < chars.Length; i++)
        {
            if (chars[i] != '\r' && chars[i] != '\n')
                chars[i] = ' ';
        }
    }

    private static IEnumerable<string> ExtractStringLiterals(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] is not ('\'' or '"'))
            {
                i++;
                continue;
            }

            var quote = text[i++];
            var builder = new StringBuilder();
            var escaped = false;
            while (i < text.Length)
            {
                var c = text[i++];
                if (!escaped && c == quote)
                    break;
                if (escaped)
                {
                    builder.Append(c);
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else
                {
                    builder.Append(c);
                }
            }

            yield return builder.ToString();
        }
    }

    private static bool IsAssetLikeString(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("assets/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SymbolIsReferenced(string source, string symbol)
    {
        var bracket = symbol.IndexOf('[');
        if (bracket < 0)
            return Regex.IsMatch(source, @"\b" + Regex.Escape(symbol) + @"\b");

        var table = symbol[..bracket];
        var key = symbol[(bracket + 1)..^1];
        return Regex.IsMatch(
            source,
            "\\b" + Regex.Escape(table) + "\\s*\\[\\s*['\"]?" + Regex.Escape(key) + "['\"]?\\s*\\]");
    }

    private static Dictionary<string, string> ReadGuidSymbols(string globalLua)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in GuidAssignmentRegex.Matches(globalLua))
            result[match.Groups[1].Value] = match.Groups[2].Value;

        foreach (Match tableMatch in GuidTableRegex.Matches(globalLua))
        {
            var tableName = tableMatch.Groups[1].Value;
            foreach (Match entry in GuidTableEntryRegex.Matches(tableMatch.Groups[2].Value))
            {
                var key = entry.Groups[1].Success ? entry.Groups[1].Value : entry.Groups[2].Value;
                result[$"{tableName}[{key}]"] = entry.Groups[3].Value;
            }
        }

        return result;
    }

    private static Dictionary<string, RuntimeGuidReference> LoadRuntimeDependencies(string? runtimeReportPath)
    {
        if (string.IsNullOrWhiteSpace(runtimeReportPath) || !File.Exists(runtimeReportPath))
            return new Dictionary<string, RuntimeGuidReference>(StringComparer.Ordinal);

        var json = File.ReadAllText(runtimeReportPath);
        var report = JsonSerializer.Deserialize<SpawnerRuntimeReport>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return report?.GuidReferences
            .GroupBy(x => x.Symbol, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, RuntimeGuidReference>(StringComparer.Ordinal);
    }

    private static string ExtractBundleModule(string lua, string moduleName)
    {
        var marker = "__bundle_register(\"" + moduleName + "\"";
        var start = lua.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            marker = "__bundle_register('" + moduleName + "'";
            start = lua.IndexOf(marker, StringComparison.Ordinal);
        }

        if (start < 0)
            return "";

        var next = lua.IndexOf("__bundle_register(", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? lua[start..] : lua[start..next];
    }

    private static string GetString(JsonObject obj, string property)
    {
        return obj[property] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text ?? ""
            : "";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}
