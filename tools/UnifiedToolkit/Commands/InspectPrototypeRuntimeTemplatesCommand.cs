using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12B-2:
/// Inventories reusable TTS runtime templates from a working save before the
/// five approved ship assemblies are serialized.
///
/// It identifies candidates for:
/// - Small ship base
/// - Large ship base
/// - Ship peg
/// - Assigned dial
///
/// No save objects are modified.
/// </summary>
public static class InspectPrototypeRuntimeTemplatesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: inspect-prototype-runtime-templates <tts-save.json> [--output <folder>]");
            return 1;
        }

        try
        {
            var savePath = Path.GetFullPath(args[0]);
            if (!File.Exists(savePath))
                throw new FileNotFoundException("TTS save was not found.", savePath);

            var output = ReadOption(args, "--output")
                ?? Path.Combine(
                    Path.GetDirectoryName(savePath) ?? ".",
                    "prototype-runtime-template-inspection");
            output = Path.GetFullPath(output);

            var root = JsonNode.Parse(File.ReadAllText(savePath))?.AsObject()
                ?? throw new InvalidDataException("Could not parse the TTS save.");

            var objects = new List<JsonObject>();
            CollectObjects(root["ObjectStates"], objects);

            var candidates = objects
                .Select((obj, index) => Analyse(obj, index))
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Index)
                .ToList();

            var selected = new RuntimeTemplateSelection
            {
                SmallBase = Select(candidates, "SmallBase"),
                LargeBase = Select(candidates, "LargeBase"),
                Peg = Select(candidates, "Peg"),
                AssignedDial = Select(candidates, "AssignedDial")
            };

            var missing = new List<string>();
            if (selected.SmallBase is null) missing.Add("SmallBase");
            if (selected.LargeBase is null) missing.Add("LargeBase");
            if (selected.Peg is null) missing.Add("Peg");
            if (selected.AssignedDial is null) missing.Add("AssignedDial");

            Directory.CreateDirectory(output);

            var manifest = new RuntimeTemplateInspectionManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceSave = savePath.Replace('\\', '/'),
                ObjectsInspected = objects.Count,
                CandidateCount = candidates.Count,
                MissingTemplateTypes = missing,
                Selected = selected,
                Candidates = candidates
            };

            var manifestPath = Path.Combine(output, "runtime-template-inspection.json");
            var csvPath = Path.Combine(output, "runtime-template-candidates.csv");
            var reportPath = Path.Combine(output, "RUNTIME-TEMPLATE-INSPECTION.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, candidates);
            WriteReport(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 12B-2 Runtime Template Inspection");
            Console.WriteLine("=======================================================");
            Console.WriteLine();
            Console.WriteLine($"TTS save:              {savePath}");
            Console.WriteLine($"Objects inspected:     {objects.Count}");
            Console.WriteLine($"Template candidates:   {candidates.Count}");
            Console.WriteLine($"Small base selected:   {selected.SmallBase?.Guid ?? "<missing>"}");
            Console.WriteLine($"Large base selected:   {selected.LargeBase?.Guid ?? "<missing>"}");
            Console.WriteLine($"Peg selected:          {selected.Peg?.Guid ?? "<missing>"}");
            Console.WriteLine($"Assigned dial selected:{selected.AssignedDial?.Guid ?? "<missing>"}");
            Console.WriteLine($"Missing template types:{missing.Count}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:              {manifestPath}");
            Console.WriteLine($"CSV:                   {csvPath}");
            Console.WriteLine($"Report:                {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Runtime templates inspected. No TTS objects or saves were modified.");

            return missing.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Runtime-template inspection failed: {ex.Message}");
            return 1;
        }
    }

    private static RuntimeTemplateCandidate Analyse(JsonObject obj, int index)
    {
        var nickname = Read(obj, "Nickname");
        var description = Read(obj, "Description");
        var name = Read(obj, "Name");
        var guid = Read(obj, "GUID");
        var lua = Read(obj, "LuaScript");
        var xml = Read(obj, "XmlUI");

        var mesh = obj["CustomMesh"] as JsonObject;
        var meshUrl = Read(mesh, "MeshURL");
        var diffuseUrl = Read(mesh, "DiffuseURL");
        var colliderUrl = Read(mesh, "ColliderURL");

        var combined = string.Join(
            " ",
            nickname,
            description,
            name,
            meshUrl,
            diffuseUrl,
            colliderUrl,
            lua,
            xml);

        var roles = new List<string>();
        var reasons = new List<string>();
        var score = 0;

        if (ContainsAny(combined, "dial", "moveset", "actset"))
        {
            roles.Add("AssignedDial");
            score += lua.Length > 0 ? 80 : 35;
            if (xml.Length > 0) score += 20;
            reasons.Add("Contains dial/runtime markers.");
        }

        if (ContainsAny(meshUrl, "base.obj", "/bases/small/", "small/base"))
        {
            roles.Add("SmallBase");
            score += 65;
            reasons.Add("Mesh path resembles a Small ship base.");
        }

        if (ContainsAny(meshUrl, "/bases/large/", "large/base"))
        {
            roles.Add("LargeBase");
            score += 70;
            reasons.Add("Mesh path resembles a Large ship base.");
        }

        if (ContainsAny(combined, "peg", "minisculebox.obj"))
        {
            roles.Add("Peg");
            score += 60;
            reasons.Add("Contains peg/collider markers.");
        }

        if (roles.Count == 0)
            return new RuntimeTemplateCandidate { Index = index };

        return new RuntimeTemplateCandidate
        {
            Index = index,
            Guid = guid,
            Name = name,
            Nickname = nickname,
            Description = description,
            CandidateRoles = roles,
            Score = score,
            MeshUrl = meshUrl,
            DiffuseUrl = diffuseUrl,
            ColliderUrl = colliderUrl,
            LuaCharacters = lua.Length,
            XmlCharacters = xml.Length,
            Reasons = reasons,
            ObjectSnapshot = obj.DeepClone().AsObject()
        };
    }

    private static RuntimeTemplateCandidate? Select(
        IEnumerable<RuntimeTemplateCandidate> candidates,
        string role) =>
        candidates
            .Where(item => item.CandidateRoles.Contains(
                role,
                StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .FirstOrDefault();

    private static void CollectObjects(JsonNode? node, List<JsonObject> objects)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
                CollectObjects(item, objects);
            return;
        }

        if (node is not JsonObject obj)
            return;

        if (obj.ContainsKey("GUID") || obj.ContainsKey("Name"))
            objects.Add(obj);

        CollectObjects(obj["ContainedObjects"], objects);
        CollectObjects(obj["States"], objects);
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Read(JsonObject? obj, string property) =>
        obj?[property]?.GetValue<string>() ?? string.Empty;

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];

        return null;
    }

    private static void WriteCsv(
        string path,
        IEnumerable<RuntimeTemplateCandidate> candidates)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "Index,GUID,Roles,Score,Name,Nickname,MeshURL,DiffuseURL,ColliderURL,LuaCharacters,XmlCharacters,Reasons");

        foreach (var item in candidates)
        {
            writer.WriteLine(string.Join(',',
                item.Index,
                Csv(item.Guid),
                Csv(string.Join('|', item.CandidateRoles)),
                item.Score,
                Csv(item.Name),
                Csv(item.Nickname),
                Csv(item.MeshUrl),
                Csv(item.DiffuseUrl),
                Csv(item.ColliderUrl),
                item.LuaCharacters,
                item.XmlCharacters,
                Csv(string.Join('|', item.Reasons))));
        }
    }

    private static void WriteReport(
        string path,
        RuntimeTemplateInspectionManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Phase 12B-2 – Runtime Template Inspection");
        writer.WriteLine();
        writer.WriteLine($"Source save: `{manifest.SourceSave}`");
        writer.WriteLine();
        writer.WriteLine("| Template | GUID | Nickname |");
        writer.WriteLine("|---|---|---|");
        WriteSelected(writer, "Small base", manifest.Selected.SmallBase);
        WriteSelected(writer, "Large base", manifest.Selected.LargeBase);
        WriteSelected(writer, "Peg", manifest.Selected.Peg);
        WriteSelected(writer, "Assigned dial", manifest.Selected.AssignedDial);
        writer.WriteLine();
        writer.WriteLine(
            "These selections are candidates for review before the prototype-save serializer copies them.");
    }

    private static void WriteSelected(
        StreamWriter writer,
        string label,
        RuntimeTemplateCandidate? candidate)
    {
        writer.WriteLine(
            $"| {label} | `{candidate?.Guid ?? ""}` | {candidate?.Nickname ?? "Missing"} |");
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}

public sealed class RuntimeTemplateInspectionManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceSave { get; init; } = string.Empty;
    public int ObjectsInspected { get; init; }
    public int CandidateCount { get; init; }
    public List<string> MissingTemplateTypes { get; init; } = new();
    public RuntimeTemplateSelection Selected { get; init; } = new();
    public List<RuntimeTemplateCandidate> Candidates { get; init; } = new();
}

public sealed class RuntimeTemplateSelection
{
    public RuntimeTemplateCandidate? SmallBase { get; init; }
    public RuntimeTemplateCandidate? LargeBase { get; init; }
    public RuntimeTemplateCandidate? Peg { get; init; }
    public RuntimeTemplateCandidate? AssignedDial { get; init; }
}

public sealed class RuntimeTemplateCandidate
{
    public int Index { get; init; }
    public string Guid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> CandidateRoles { get; init; } = new();
    public int Score { get; init; }
    public string MeshUrl { get; init; } = string.Empty;
    public string DiffuseUrl { get; init; } = string.Empty;
    public string ColliderUrl { get; init; } = string.Empty;
    public int LuaCharacters { get; init; }
    public int XmlCharacters { get; init; }
    public List<string> Reasons { get; init; } = new();
    public JsonObject ObjectSnapshot { get; init; } = new();
}
