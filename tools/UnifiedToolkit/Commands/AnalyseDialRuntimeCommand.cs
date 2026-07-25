using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

public static partial class AnalyseDialRuntimeCommand
{
    private const string DefaultOutputFolderName = "dial-runtime-analysis";

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit analyse-dial-runtime <tts-save.json> [--output <folder>]");
            return 1;
        }

        var savePath = Path.GetFullPath(args[0]);
        var outputFolder = ResolveOutputFolder(args, savePath);

        if (!File.Exists(savePath))
        {
            Console.WriteLine($"TTS save not found: {savePath}");
            return 1;
        }

        Console.WriteLine("UnifiedToolkit Phase 11B-1 Dial Runtime Analysis");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine($"TTS save:      {savePath}");
        Console.WriteLine($"Output folder: {outputFolder}");
        Console.WriteLine();

        try
        {
            Directory.CreateDirectory(outputFolder);
            var json = File.ReadAllText(savePath, Encoding.UTF8);
            using var document = JsonDocument.Parse(json);

            var allObjects = new List<ObjectLocation>();
            if (document.RootElement.TryGetProperty("ObjectStates", out var objectStates) &&
                objectStates.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in objectStates.EnumerateArray())
                {
                    WalkObject(item, $"ObjectStates[{index}]", allObjects);
                    index++;
                }
            }

            var dials = allObjects
                .Where(x => IsDialObject(x.Element))
                .Select((x, index) => AnalyseDial(x, index + 1, outputFolder))
                .ToList();

            var report = BuildReport(savePath, allObjects.Count, dials);
            WriteOutputs(outputFolder, report);
            PrintSummary(report, outputFolder);

            return dials.Count == 0 ? 2 : 0;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid TTS JSON: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dial analysis failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveOutputFolder(string[] args, string savePath)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var saveFolder = Path.GetDirectoryName(savePath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(saveFolder, DefaultOutputFolderName);
    }

    private static void WalkObject(JsonElement element, string path, List<ObjectLocation> output)
    {
        output.Add(new ObjectLocation(path, element.Clone()));

        if (element.TryGetProperty("ContainedObjects", out var contained) && contained.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in contained.EnumerateArray())
            {
                WalkObject(child, $"{path}/ContainedObjects[{index}]", output);
                index++;
            }
        }

        if (element.TryGetProperty("States", out var states) && states.ValueKind == JsonValueKind.Object)
        {
            foreach (var state in states.EnumerateObject())
            {
                if (state.Value.ValueKind == JsonValueKind.Object)
                {
                    WalkObject(state.Value, $"{path}/States[{state.Name}]", output);
                }
            }
        }
    }

    private static bool IsDialObject(JsonElement element)
    {
        if (!element.TryGetProperty("CustomMesh", out var customMesh) || customMesh.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var mesh = GetString(customMesh, "MeshURL");
        var collider = GetString(customMesh, "ColliderURL");

        return mesh.Contains("dialmodel", StringComparison.OrdinalIgnoreCase) ||
               collider.Contains("dialcollider", StringComparison.OrdinalIgnoreCase);
    }

    private static DialRuntimeEntry AnalyseDial(ObjectLocation location, int number, string outputFolder)
    {
        var element = location.Element;
        var customMesh = element.TryGetProperty("CustomMesh", out var meshElement) && meshElement.ValueKind == JsonValueKind.Object
            ? meshElement
            : default;

        var guid = GetString(element, "GUID");
        var nickname = GetString(element, "Nickname").Trim();
        var description = GetString(element, "Description");
        var lua = GetString(element, "LuaScript");
        var luaState = GetString(element, "LuaScriptState");
        var xml = GetString(element, "XmlUI");

        var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(nickname) ? $"dial-{number}" : nickname);
        var prefix = $"{number:00}-{safeName}-{(string.IsNullOrWhiteSpace(guid) ? "noguid" : guid)}";

        var luaFile = string.Empty;
        if (!string.IsNullOrWhiteSpace(lua))
        {
            luaFile = $"{prefix}.lua";
            File.WriteAllText(Path.Combine(outputFolder, luaFile), lua, new UTF8Encoding(false));
        }

        var xmlFile = string.Empty;
        if (!string.IsNullOrWhiteSpace(xml))
        {
            xmlFile = $"{prefix}.xml";
            File.WriteAllText(Path.Combine(outputFolder, xmlFile), xml, new UTF8Encoding(false));
        }

        var stateFile = string.Empty;
        if (!string.IsNullOrWhiteSpace(luaState))
        {
            stateFile = $"{prefix}.state.json";
            File.WriteAllText(Path.Combine(outputFolder, stateFile), luaState, new UTF8Encoding(false));
        }

        var combinedText = string.Join('\n', new[] { lua, xml, luaState, description });
        var urls = UrlRegex().Matches(combinedText)
            .Select(x => x.Value.TrimEnd('"', '\'', ')', ']', '}', ','))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actionTerms = FindTerms(combinedText, ActionTerms);
        var maneuverTerms = FindTerms(combinedText, ManeuverTerms);
        var buttonLabels = ExtractButtonLabels(element);

        return new DialRuntimeEntry
        {
            Number = number,
            JsonPath = location.Path,
            Guid = guid,
            Name = GetString(element, "Name"),
            Nickname = nickname,
            Description = description,
            MeshUrl = GetString(customMesh, "MeshURL"),
            ColliderUrl = GetString(customMesh, "ColliderURL"),
            DiffuseUrl = GetString(customMesh, "DiffuseURL"),
            NormalUrl = GetString(customMesh, "NormalURL"),
            MaterialIndex = GetInt(customMesh, "MaterialIndex"),
            LuaCharacters = lua.Length,
            XmlCharacters = xml.Length,
            LuaStateCharacters = luaState.Length,
            LuaFile = luaFile,
            XmlFile = xmlFile,
            LuaStateFile = stateFile,
            ReferencedUrls = urls,
            ButtonLabels = buttonLabels,
            ActionTerms = actionTerms,
            ManeuverTerms = maneuverTerms
        };
    }

    private static List<string> ExtractButtonLabels(JsonElement element)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (element.TryGetProperty("Buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array)
        {
            foreach (var button in buttons.EnumerateArray())
            {
                var label = GetString(button, "Label").Trim();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels.Add(label);
                }
            }
        }

        return labels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FindTerms(string text, IEnumerable<string> terms)
    {
        return terms
            .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(term => term, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DialRuntimeReport BuildReport(string savePath, int objectsInspected, List<DialRuntimeEntry> dials)
    {
        var uniqueMeshes = dials.Select(x => x.MeshUrl).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var uniqueColliders = dials.Select(x => x.ColliderUrl).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var uniqueSkins = dials.Select(x => x.DiffuseUrl).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var uniqueUrls = dials.SelectMany(x => x.ReferencedUrls).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var actionTerms = dials.SelectMany(x => x.ActionTerms).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var maneuverTerms = dials.SelectMany(x => x.ManeuverTerms).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        return new DialRuntimeReport
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceSave = savePath.Replace('\\', '/'),
            ObjectsInspected = objectsInspected,
            DialObjectsFound = dials.Count,
            UniqueMeshes = uniqueMeshes,
            UniqueColliders = uniqueColliders,
            UniqueFactionSkins = uniqueSkins,
            ReferencedUrls = uniqueUrls,
            ActionTerms = actionTerms,
            ManeuverTerms = maneuverTerms,
            Dials = dials
        };
    }

    private static void WriteOutputs(string outputFolder, DialRuntimeReport report)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(outputFolder, "dial-runtime-analysis.json"),
            JsonSerializer.Serialize(report, options),
            new UTF8Encoding(false));

        var csv = new StringBuilder();
        csv.AppendLine("Number,GUID,Nickname,JsonPath,MeshURL,ColliderURL,DiffuseURL,LuaCharacters,XmlCharacters,LuaStateCharacters,LuaFile,XmlFile,LuaStateFile,Buttons,ActionTerms,ManeuverTerms");
        foreach (var dial in report.Dials)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                dial.Number.ToString(),
                Csv(dial.Guid),
                Csv(dial.Nickname),
                Csv(dial.JsonPath),
                Csv(dial.MeshUrl),
                Csv(dial.ColliderUrl),
                Csv(dial.DiffuseUrl),
                dial.LuaCharacters.ToString(),
                dial.XmlCharacters.ToString(),
                dial.LuaStateCharacters.ToString(),
                Csv(dial.LuaFile),
                Csv(dial.XmlFile),
                Csv(dial.LuaStateFile),
                Csv(string.Join(" | ", dial.ButtonLabels)),
                Csv(string.Join(" | ", dial.ActionTerms)),
                Csv(string.Join(" | ", dial.ManeuverTerms))
            }));
        }
        File.WriteAllText(Path.Combine(outputFolder, "dial-runtime-objects.csv"), csv.ToString(), new UTF8Encoding(false));

        var markdown = new StringBuilder();
        markdown.AppendLine("# Phase 11B-1 Dial Runtime Analysis");
        markdown.AppendLine();
        markdown.AppendLine($"- Source save: `{report.SourceSave}`");
        markdown.AppendLine($"- Objects inspected: {report.ObjectsInspected}");
        markdown.AppendLine($"- Dial objects found: {report.DialObjectsFound}");
        markdown.AppendLine($"- Unique dial meshes: {report.UniqueMeshes.Count}");
        markdown.AppendLine($"- Unique colliders: {report.UniqueColliders.Count}");
        markdown.AppendLine($"- Unique faction skins: {report.UniqueFactionSkins.Count}");
        markdown.AppendLine();
        markdown.AppendLine("## Runtime conclusion");
        markdown.AppendLine();
        markdown.AppendLine("The physical dial is identified by its dial mesh/collider. Its `DiffuseURL` is the generic faction skin. Ship identity, manoeuvre selection and action controls are supplied by Lua/UI state rather than by the physical mesh texture.");
        markdown.AppendLine();
        markdown.AppendLine("## Faction skins");
        markdown.AppendLine();
        foreach (var skin in report.UniqueFactionSkins) markdown.AppendLine($"- `{skin}`");
        markdown.AppendLine();
        markdown.AppendLine("## Detected action terms");
        markdown.AppendLine();
        foreach (var action in report.ActionTerms) markdown.AppendLine($"- {action}");
        markdown.AppendLine();
        markdown.AppendLine("## Detected manoeuvre terms");
        markdown.AppendLine();
        foreach (var maneuver in report.ManeuverTerms) markdown.AppendLine($"- {maneuver}");
        markdown.AppendLine();
        markdown.AppendLine("## Dial objects");
        markdown.AppendLine();
        foreach (var dial in report.Dials)
        {
            markdown.AppendLine($"### {dial.Number:00} — {dial.Nickname} (`{dial.Guid}`)");
            markdown.AppendLine();
            markdown.AppendLine($"- JSON path: `{dial.JsonPath}`");
            markdown.AppendLine($"- Mesh: `{dial.MeshUrl}`");
            markdown.AppendLine($"- Collider: `{dial.ColliderUrl}`");
            markdown.AppendLine($"- Faction skin: `{dial.DiffuseUrl}`");
            markdown.AppendLine($"- Lua characters: {dial.LuaCharacters}");
            markdown.AppendLine($"- XML characters: {dial.XmlCharacters}");
            markdown.AppendLine($"- Lua state characters: {dial.LuaStateCharacters}");
            if (dial.LuaFile.Length > 0) markdown.AppendLine($"- Extracted Lua: `{dial.LuaFile}`");
            if (dial.XmlFile.Length > 0) markdown.AppendLine($"- Extracted XML: `{dial.XmlFile}`");
            if (dial.LuaStateFile.Length > 0) markdown.AppendLine($"- Extracted state: `{dial.LuaStateFile}`");
            markdown.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputFolder, "DIAL-RUNTIME-ANALYSIS-REPORT.md"), markdown.ToString(), new UTF8Encoding(false));
    }

    private static void PrintSummary(DialRuntimeReport report, string outputFolder)
    {
        Console.WriteLine($"Objects inspected:       {report.ObjectsInspected}");
        Console.WriteLine($"Dial objects found:      {report.DialObjectsFound}");
        Console.WriteLine($"Unique dial meshes:      {report.UniqueMeshes.Count}");
        Console.WriteLine($"Unique colliders:        {report.UniqueColliders.Count}");
        Console.WriteLine($"Unique faction skins:    {report.UniqueFactionSkins.Count}");
        Console.WriteLine($"Detected action terms:   {report.ActionTerms.Count}");
        Console.WriteLine($"Detected manoeuvre terms:{report.ManeuverTerms.Count}");
        Console.WriteLine();
        Console.WriteLine($"Report:   {Path.Combine(outputFolder, "DIAL-RUNTIME-ANALYSIS-REPORT.md")}");
        Console.WriteLine($"Manifest: {Path.Combine(outputFolder, "dial-runtime-analysis.json")}");
        Console.WriteLine($"Objects:  {Path.Combine(outputFolder, "dial-runtime-objects.csv")}");
        Console.WriteLine();
        Console.WriteLine("Dial runtime analysis completed. No TTS objects were modified.");
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        }
        return 0;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        cleaned = WhitespaceRegex().Replace(cleaned, "-");
        return string.IsNullOrWhiteSpace(cleaned) ? "dial" : cleaned.ToLowerInvariant();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static readonly string[] ActionTerms =
    {
        "barrel roll", "boost", "calculate", "cloak", "coordinate", "evade", "focus",
        "jam", "reinforce", "reload", "rotate arc", "slam", "target lock", "lock"
    };

    private static readonly string[] ManeuverTerms =
    {
        "bank", "k-turn", "koiogran", "reverse", "segnor", "s-loop", "stationary",
        "straight", "tallon", "turn"
    };

    [GeneratedRegex("https?://[^\\s\"'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record ObjectLocation(string Path, JsonElement Element);

    private sealed class DialRuntimeReport
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; init; }
        public string SourceSave { get; init; } = string.Empty;
        public int ObjectsInspected { get; init; }
        public int DialObjectsFound { get; init; }
        public List<string> UniqueMeshes { get; init; } = [];
        public List<string> UniqueColliders { get; init; } = [];
        public List<string> UniqueFactionSkins { get; init; } = [];
        public List<string> ReferencedUrls { get; init; } = [];
        public List<string> ActionTerms { get; init; } = [];
        public List<string> ManeuverTerms { get; init; } = [];
        public List<DialRuntimeEntry> Dials { get; init; } = [];
    }

    private sealed class DialRuntimeEntry
    {
        public int Number { get; init; }
        public string JsonPath { get; init; } = string.Empty;
        public string Guid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Nickname { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string MeshUrl { get; init; } = string.Empty;
        public string ColliderUrl { get; init; } = string.Empty;
        public string DiffuseUrl { get; init; } = string.Empty;
        public string NormalUrl { get; init; } = string.Empty;
        public int MaterialIndex { get; init; }
        public int LuaCharacters { get; init; }
        public int XmlCharacters { get; init; }
        public int LuaStateCharacters { get; init; }
        public string LuaFile { get; init; } = string.Empty;
        public string XmlFile { get; init; } = string.Empty;
        public string LuaStateFile { get; init; } = string.Empty;
        public List<string> ReferencedUrls { get; init; } = [];
        public List<string> ButtonLabels { get; init; } = [];
        public List<string> ActionTerms { get; init; } = [];
        public List<string> ManeuverTerms { get; init; } = [];
    }
}
