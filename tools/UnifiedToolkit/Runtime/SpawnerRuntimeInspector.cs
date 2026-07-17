using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Runtime;

public static class SpawnerRuntimeInspector
{
    private static readonly Regex NamedFunctionRegex =
        new(@"\bfunction\s+([A-Za-z_][A-Za-z0-9_\.:]*)\s*\(", RegexOptions.Compiled);

    private static readonly Regex AssignedFunctionRegex =
        new(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*function\s*\(", RegexOptions.Compiled);

    private static readonly Regex DirectLiteralGuidRegex =
        new("""getObjectFromGUID\s*\(\s*['"]([0-9A-Fa-f]{6})['"]\s*\)""", RegexOptions.Compiled);

    private static readonly Regex DirectSymbolGuidRegex =
        new(@"\bgetObjectFromGUID\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", RegexOptions.Compiled);

    private static readonly Regex DirectTableGuidRegex =
        new("""\bgetObjectFromGUID\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\[\s*['"]([^'"]+)['"]\s*\]\s*\)""",
            RegexOptions.Compiled);

    private static readonly Regex AssignmentRegex =
        new("""(?m)^\s*(?:local\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*['"]([0-9A-Fa-f]{6})['"]\s*,?\s*$""",
            RegexOptions.Compiled);

    private static readonly Regex TableBlockRegex =
        new(@"(?ms)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{(.*?)^\s*\}", RegexOptions.Compiled);

    private static readonly Regex TableEntryRegex =
        new("""(?:\[\s*['"]([^'"]+)['"]\s*\]|([A-Za-z_][A-Za-z0-9_]*))\s*=\s*['"]([0-9A-Fa-f]{6})['"]""",
            RegexOptions.Compiled);

    public static SpawnerRuntimeReport Inspect(string savePath)
    {
        var root = JsonNode.Parse(File.ReadAllText(savePath))?.AsObject()
            ?? throw new InvalidOperationException("The TTS save could not be parsed as a JSON object.");

        var report = new SpawnerRuntimeReport { SourceSave = Path.GetFullPath(savePath) };

        var objectStates = root["ObjectStates"]?.AsArray() ?? new JsonArray();
        foreach (var node in objectStates)
        {
            if (node is JsonObject obj)
                VisitObject(obj, "ObjectStates", report.Objects);
        }

        var globalLua = GetDecodedString(root, "LuaScript");
        var module = ExtractBundleModule(globalLua, report.ModuleName);
        report.Summary.ModuleCharacters = module.Length;

        var functionMap = new Dictionary<string, RuntimeFunctionRecord>(StringComparer.Ordinal);
        foreach (Match match in NamedFunctionRegex.Matches(module))
            AddFunction(functionMap, match.Groups[1].Value, match.Index);
        foreach (Match match in AssignedFunctionRegex.Matches(module))
            AddFunction(functionMap, match.Groups[1].Value, match.Index);
        report.Functions = functionMap.Values.OrderBy(x => x.CharacterOffset).ToList();

        var globalSymbols = ReadGlobalGuidSymbols(globalLua);
        var references = ReadSpawnerReferences(module, globalSymbols);

        var byGuid = report.Objects
            .Where(x => x.Guid.Length > 0)
            .GroupBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in references)
        {
            if (!byGuid.TryGetValue(item.Guid, out var obj))
                continue;

            item.Resolved = true;
            item.ObjectName = obj.Name;
            item.ObjectNickname = obj.Nickname;
            item.ObjectPath = obj.Path;
            item.Assets = obj.Assets.ToList();
        }

        report.GuidReferences = references
            .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        report.Summary.TotalObjects = report.Objects.Count;
        report.Summary.ObjectsWithLua = report.Objects.Count(x => x.HasLua);
        report.Summary.ObjectsWithCustomAssets = report.Objects.Count(x => x.Assets.Count > 0);
        report.Summary.TotalAssetUrls = report.Objects.Sum(x => x.Assets.Count);
        report.Summary.FunctionCount = report.Functions.Count;
        report.Summary.GuidReferenceCount = report.GuidReferences.Count;
        report.Summary.ResolvedGuidCount = report.GuidReferences.Count(x => x.Resolved);
        report.Summary.UnresolvedGuidCount = report.GuidReferences.Count(x => !x.Resolved);

        report.Findings.Add(module.Length > 0
            ? "The Game.Component.Spawner.Spawner bundle module was located and extracted."
            : "The Game.Component.Spawner.Spawner bundle module was not located.");

        report.Findings.Add(
            $"Catalogued {report.Summary.TotalAssetUrls} asset URLs across " +
            $"{report.Summary.ObjectsWithCustomAssets} TTS objects.");

        report.Findings.Add(
            $"Resolved {report.Summary.ResolvedGuidCount} of " +
            $"{report.Summary.GuidReferenceCount} GUID references used by the spawner.");

        report.Findings.Add(
            "Spawner GUID symbols are resolved from the complete Global Lua, including global tables such as bagGuids.");

        report.Findings.Add(
            "No generated ship objects or First Edition payloads are emitted by this inspection command.");

        return report;
    }

    private static Dictionary<string, string> ReadGlobalGuidSymbols(string globalLua)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in AssignmentRegex.Matches(globalLua))
            result[match.Groups[1].Value] = match.Groups[2].Value;

        foreach (Match tableMatch in TableBlockRegex.Matches(globalLua))
        {
            var tableName = tableMatch.Groups[1].Value;
            var tableBody = tableMatch.Groups[2].Value;

            foreach (Match entry in TableEntryRegex.Matches(tableBody))
            {
                var key = entry.Groups[1].Success
                    ? entry.Groups[1].Value
                    : entry.Groups[2].Value;

                result[$"{tableName}[{key}]"] = entry.Groups[3].Value;
            }
        }

        return result;
    }

    private static List<RuntimeGuidReference> ReadSpawnerReferences(
        string module,
        IReadOnlyDictionary<string, string> globalSymbols)
    {
        var result = new Dictionary<string, RuntimeGuidReference>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in DirectLiteralGuidRegex.Matches(module))
        {
            AddReference(
                result,
                $"literal:{match.Groups[1].Value}",
                match.Groups[1].Value,
                "Direct literal getObjectFromGUID",
                true);
        }

        foreach (Match match in DirectTableGuidRegex.Matches(module))
        {
            var table = match.Groups[1].Value;
            var key = match.Groups[2].Value;
            var symbol = $"{table}[{key}]";

            if (globalSymbols.TryGetValue(symbol, out var guid))
                AddReference(result, symbol, guid, "Global GUID table entry", true);
        }

        foreach (Match match in DirectSymbolGuidRegex.Matches(module))
        {
            var symbol = match.Groups[1].Value;

            if (globalSymbols.TryGetValue(symbol, out var guid))
                AddReference(result, symbol, guid, "Global GUID variable", true);
        }

        return result.Values.ToList();
    }

    private static void AddFunction(
        IDictionary<string, RuntimeFunctionRecord> functions,
        string name,
        int offset)
    {
        if (!functions.ContainsKey(name))
            functions[name] = new RuntimeFunctionRecord
            {
                Name = name,
                CharacterOffset = offset
            };
    }

    private static void AddReference(
        IDictionary<string, RuntimeGuidReference> references,
        string symbol,
        string guid,
        string kind,
        bool usedBySpawnerModule)
    {
        var key = symbol + "|" + guid;

        if (references.ContainsKey(key))
            return;

        references[key] = new RuntimeGuidReference
        {
            Symbol = symbol,
            Guid = guid,
            ReferenceKind = kind,
            UsedBySpawnerModule = usedBySpawnerModule
        };
    }

    private static string ExtractBundleModule(string lua, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return "";

        var marker = "__bundle_register(\"" + moduleName + "\"";
        var start = lua.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
        {
            marker = "__bundle_register('" + moduleName + "'";
            start = lua.IndexOf(marker, StringComparison.Ordinal);
        }

        if (start < 0)
            return "";

        var next = lua.IndexOf(
            "__bundle_register(",
            start + marker.Length,
            StringComparison.Ordinal);

        return next < 0 ? lua[start..] : lua[start..next];
    }

    private static void VisitObject(
        JsonObject obj,
        string path,
        ICollection<RuntimeObjectRecord> output)
    {
        var guid = GetDecodedString(obj, "GUID");
        var name = GetDecodedString(obj, "Name");
        var nickname = GetDecodedString(obj, "Nickname");

        var record = new RuntimeObjectRecord
        {
            Guid = guid,
            Name = name,
            Nickname = nickname,
            Description = GetDecodedString(obj, "Description"),
            Path = path + "/" +
                   (guid.Length > 0 ? guid : "no-guid") + " " +
                   (nickname.Length > 0 ? nickname : name),
            HasLua = !string.IsNullOrWhiteSpace(GetDecodedString(obj, "LuaScript")),
            HasXml = !string.IsNullOrWhiteSpace(GetDecodedString(obj, "XmlUI"))
        };

        AddAssetContainer(obj, "CustomMesh", record.Assets);
        AddAssetContainer(obj, "CustomImage", record.Assets);
        AddAssetContainer(obj, "CustomPDF", record.Assets);
        AddAssetContainer(obj, "CustomAssetbundle", record.Assets);
        AddAssetContainer(obj, "CustomObject", record.Assets);

        AddDeckAssets(obj, record.Assets);

        output.Add(record);

        if (obj["ContainedObjects"] is not JsonArray children)
            return;

        var index = 0;
        foreach (var child in children)
        {
            if (child is JsonObject childObj)
            {
                VisitObject(
                    childObj,
                    record.Path + $"/ContainedObjects[{index}]",
                    output);
            }

            index++;
        }
    }

    private static void AddAssetContainer(
        JsonObject obj,
        string property,
        ICollection<RuntimeAssetRecord> assets)
    {
        if (obj[property] is not JsonObject container)
            return;

        AddKnownAsset(container, property, assets, "MeshURL", "Mesh");
        AddKnownAsset(container, property, assets, "DiffuseURL", "Diffuse");
        AddKnownAsset(container, property, assets, "NormalURL", "Normal");
        AddKnownAsset(container, property, assets, "ColliderURL", "Collider");
        AddKnownAsset(container, property, assets, "ImageURL", "Image");
        AddKnownAsset(container, property, assets, "ImageSecondaryURL", "ImageSecondary");
        AddKnownAsset(container, property, assets, "PDFUrl", "PDF");
        AddKnownAsset(container, property, assets, "AssetbundleURL", "AssetBundle");
        AddKnownAsset(container, property, assets, "AssetbundleSecondaryURL", "AssetBundleSecondary");
    }

    private static void AddDeckAssets(
        JsonObject obj,
        ICollection<RuntimeAssetRecord> assets)
    {
        if (obj["CustomDeck"] is not JsonObject customDeck)
            return;

        foreach (var pair in customDeck)
        {
            if (pair.Value is not JsonObject deck)
                continue;

            var container = $"CustomDeck[{pair.Key}]";
            AddKnownAsset(deck, container, assets, "FaceURL", "Face");
            AddKnownAsset(deck, container, assets, "BackURL", "Back");
        }
    }

    private static void AddKnownAsset(
        JsonObject container,
        string containerName,
        ICollection<RuntimeAssetRecord> assets,
        string property,
        string role)
    {
        var value = GetDecodedString(container, property);

        if (string.IsNullOrWhiteSpace(value))
            return;

        assets.Add(new RuntimeAssetRecord
        {
            Container = containerName,
            Role = role,
            Url = value
        });
    }

    private static string GetDecodedString(JsonObject obj, string property)
    {
        if (!obj.TryGetPropertyValue(property, out var node) || node is null)
            return "";

        return node is JsonValue value &&
               value.TryGetValue<string>(out var text)
            ? text ?? ""
            : "";
    }
}
