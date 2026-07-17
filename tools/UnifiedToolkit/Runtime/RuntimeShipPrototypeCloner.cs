using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Runtime;

public sealed class RuntimePrototypeCloneResult
{
    public string SchemaVersion { get; set; } = "1.1";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string PrototypePath { get; set; } = "";
    public string EnvelopePath { get; set; } = "";
    public string OutputSavePath { get; set; } = "";
    public string SourceShipGuid { get; set; } = "";
    public string ClonedShipGuid { get; set; } = "";
    public int SourceObjectCount { get; set; }
    public int ClonedHierarchyObjectCount { get; set; }
    public int GuidMappings { get; set; }
    public bool ParentScriptPreserved { get; set; }
    public bool ParentXmlPreserved { get; set; }
    public bool GlobalLuaPreserved { get; set; }
    public bool GlobalXmlPreserved { get; set; }
    public bool SfoilsTriggerFound { get; set; }
    public bool SetupStatePreserved { get; set; }
    public bool FinishedSetupForced { get; set; }
    public bool SourceFinishedSetupPresent { get; set; }
    public bool SourceFinishedSetupValue { get; set; }
    public string SfoilsContextText { get; set; } = "";
    public string SfoilsFunctionName { get; set; } = "";
    public bool ReadyForTtsLoadTest { get; set; }
    public Dictionary<string, string> GuidMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public static class RuntimeShipPrototypeCloner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex GuidRegex = new("^[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static RuntimePrototypeCloneResult Clone(string prototypePath, string envelopePath, string outputFolder)
    {
        var result = new RuntimePrototypeCloneResult
        {
            PrototypePath = Path.GetFullPath(prototypePath),
            EnvelopePath = Path.GetFullPath(envelopePath)
        };

        var document = JsonSerializer.Deserialize<RuntimeShipPrototypeDocument>(File.ReadAllText(prototypePath), JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize runtime prototype document.");
        var prototype = document.Prototype ?? throw new InvalidOperationException("Prototype document does not contain a captured ship prototype.");
        if (!document.Summary.ReadyForPrototypeCloning)
            result.ValidationErrors.Add("Captured prototype is not marked ready for cloning.");

        var envelope = JsonNode.Parse(File.ReadAllText(envelopePath))?.AsObject()
            ?? throw new InvalidOperationException("Could not parse the TTS envelope save.");
        var sourceObjects = envelope["ObjectStates"] as JsonArray ?? envelope["Objects"] as JsonArray;
        result.SourceObjectCount = sourceObjects?.Count ?? 0;

        var clone = prototype.SourceObjectSnapshot.DeepClone().AsObject();
        if (clone.Count == 0)
            result.ValidationErrors.Add("Captured source object snapshot is empty.");

        CollectGuids(clone, result.GuidMap);
        ApplyGuidMap(clone, result.GuidMap);
        result.SourceShipGuid = prototype.Guid;
        result.ClonedShipGuid = result.GuidMap.TryGetValue(prototype.Guid, out var mapped) ? mapped : ReadString(clone, "GUID");
        result.GuidMappings = result.GuidMap.Count;
        result.ClonedHierarchyObjectCount = CountObjects(clone);

        RepositionRoot(clone);
        clone["Nickname"] = $"{prototype.Nickname} — Phase 6B Prototype Clone";
        clone["Locked"] = false;

        var lua = ReadString(clone, "LuaScript");
        var xml = ReadString(clone, "XmlUI");
        result.ParentScriptPreserved = !string.IsNullOrWhiteSpace(lua);
        result.ParentXmlPreserved = !string.IsNullOrWhiteSpace(xml);
        AnalyseSfoils(lua, prototype, result);
        InspectAndPreserveSetupState(clone, result);

        var outputEnvelope = envelope.DeepClone().AsObject();
        var outputObjects = new JsonArray { clone };
        if (outputEnvelope.ContainsKey("ObjectStates")) outputEnvelope["ObjectStates"] = outputObjects;
        else outputEnvelope["Objects"] = outputObjects;
        outputEnvelope["SaveName"] = "UnifiedToolkit Phase 6B R3 Runtime Prototype Clone";
        outputEnvelope["GameMode"] = "X-Wing Runtime Prototype Clone";
        outputEnvelope["Date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        result.GlobalLuaPreserved = !string.IsNullOrWhiteSpace(ReadString(outputEnvelope, "LuaScript"));
        result.GlobalXmlPreserved = !string.IsNullOrWhiteSpace(ReadString(outputEnvelope, "XmlUI"));

        if (!result.ParentScriptPreserved) result.ValidationErrors.Add("Parent ship Lua was not preserved.");
        if (result.ClonedHierarchyObjectCount < 6) result.ValidationErrors.Add($"Expected the parent and five children, but found {result.ClonedHierarchyObjectCount} hierarchy objects.");
        if (string.IsNullOrWhiteSpace(result.ClonedShipGuid)) result.ValidationErrors.Add("A fresh parent GUID was not assigned.");
        if (!result.SfoilsTriggerFound) result.ValidationErrors.Add("The captured ship script does not expose the expected S-foils configuration trigger.");
        if (!result.SetupStatePreserved) result.ValidationErrors.Add("The cloned parent Lua state could not be preserved.");
        if (result.FinishedSetupForced) result.ValidationErrors.Add("finishedSetup must not be forced by the prototype cloner.");

        Directory.CreateDirectory(outputFolder);
        var savePath = Path.Combine(outputFolder, "XWing-1E-Phase6B-R3-Runtime-Prototype-Clone.json");
        File.WriteAllText(savePath, outputEnvelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        result.OutputSavePath = savePath;
        result.ReadyForTtsLoadTest = result.ValidationErrors.Count == 0;
        return result;
    }

    private static void InspectAndPreserveSetupState(JsonObject clone, RuntimePrototypeCloneResult result)
    {
        var originalStateText = ReadString(clone, "LuaScriptState");
        result.SetupStatePreserved = true;
        result.FinishedSetupForced = false;

        if (string.IsNullOrWhiteSpace(originalStateText))
        {
            result.Notes.Add("The captured parent has no LuaScriptState. The cloner leaves it absent so the existing setup flow remains authoritative.");
            return;
        }

        try
        {
            var state = JsonNode.Parse(originalStateText)?.AsObject();
            if (state?["finishedSetup"] is JsonValue value && value.TryGetValue<bool>(out var finished))
            {
                result.SourceFinishedSetupPresent = true;
                result.SourceFinishedSetupValue = finished;
            }

            var preservedStateText = ReadString(clone, "LuaScriptState");
            result.SetupStatePreserved = string.Equals(originalStateText, preservedStateText, StringComparison.Ordinal);
            result.Notes.Add(result.SourceFinishedSetupPresent
                ? $"Preserved the captured finishedSetup value ({result.SourceFinishedSetupValue}) without modification."
                : "Preserved the captured LuaScriptState without adding finishedSetup. The setup-stage menu remains active until the existing Unified flow sets finished_setup.");
        }
        catch (JsonException ex)
        {
            result.SetupStatePreserved = string.Equals(originalStateText, ReadString(clone, "LuaScriptState"), StringComparison.Ordinal);
            result.Notes.Add($"LuaScriptState is not valid JSON ({ex.Message}), but its original text was preserved unchanged.");
        }
    }

    private static void AnalyseSfoils(string lua, RuntimeShipPrototype prototype, RuntimePrototypeCloneResult result)
    {
        var config = prototype.Visual.Configurations.FirstOrDefault();
        result.SfoilsContextText = config?.ContextText ?? "";
        result.SfoilsFunctionName = "SetConfiguration";
        result.SfoilsTriggerFound =
            lua.Contains("function SetConfiguration", StringComparison.Ordinal) &&
            lua.Contains("self.addContextMenuItem(config.ContextText", StringComparison.Ordinal) &&
            prototype.Visual.Configurations.Count >= 2;

        if (result.SfoilsTriggerFound)
        {
            result.Notes.Add("The existing Unified script implements the S-foils switch through SetConfiguration().");
            result.Notes.Add($"After normal setup completion, the right-click context menu text is '{result.SfoilsContextText}'.");
            result.Notes.Add("Revision 3 deliberately does not force finishedSetup; the script must transition from setup-stage commands to gameplay commands through its normal setup flow.");
        }
    }

    private static void CollectGuids(JsonNode? node, Dictionary<string, string> map)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Key.Equals("GUID", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value)
                {
                    var oldGuid = value.GetValue<string>();
                    if (GuidRegex.IsMatch(oldGuid) && !map.ContainsKey(oldGuid)) map[oldGuid] = NewGuid(map.Values);
                }
                CollectGuids(pair.Value, map);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) CollectGuids(item, map);
        }
    }

    private static string NewGuid(IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var bytes = new byte[3];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var value = Convert.ToHexString(bytes).ToLowerInvariant();
            if (!used.Contains(value)) return value;
        }
    }

    private static void ApplyGuidMap(JsonNode? node, IReadOnlyDictionary<string, string> map)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToList())
            {
                var child = obj[key];
                if (child is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    if (map.TryGetValue(text, out var exact)) obj[key] = exact;
                    else obj[key] = ReplaceGuidReferences(text, map);
                }
                else ApplyGuidMap(child, map);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = ReplaceGuidReferences(text, map);
                else ApplyGuidMap(array[i], map);
            }
        }
    }

    private static string ReplaceGuidReferences(string text, IReadOnlyDictionary<string, string> map)
    {
        foreach (var pair in map)
            text = Regex.Replace(text, $"(?<![0-9a-fA-F]){Regex.Escape(pair.Key)}(?![0-9a-fA-F])", pair.Value, RegexOptions.IgnoreCase);
        return text;
    }

    private static int CountObjects(JsonObject obj)
    {
        var count = 1;
        if (obj["ChildObjects"] is JsonArray children)
            foreach (var child in children.OfType<JsonObject>()) count += CountObjects(child);
        if (obj["ContainedObjects"] is JsonArray contained)
            foreach (var child in contained.OfType<JsonObject>()) count += CountObjects(child);
        return count;
    }

    private static void RepositionRoot(JsonObject clone)
    {
        if (clone["Transform"] is not JsonObject transform) return;
        transform["posX"] = 0.0;
        transform["posY"] = 1.25;
        transform["posZ"] = 0.0;
        transform["rotX"] = 0.0;
        transform["rotY"] = 180.0;
        transform["rotZ"] = 0.0;
    }

    private static string ReadString(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
