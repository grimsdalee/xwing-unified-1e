using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Produces a read-only visual review save for the three required First Edition
/// obstacle sets. Only the standard Unified 2.5 forms are displayed; optional
/// Pride alternate states and gameplay Lua are deliberately excluded.
/// </summary>
public static class PrepareFirstEditionObstacleSetReviewCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly ObstacleSetSeed[] Sets =
    [
        new("core-asteroids", "Core Set Asteroids",
        [
            new("core-asteroid-1", "Core Asteroid 1", "Asteroid 1", "9564c7"),
            new("core-asteroid-2", "Core Asteroid 2", "Asteroid 2", "ed9fcc"),
            new("core-asteroid-3", "Core Asteroid 3", "Asteroid 3", "6925de"),
            new("core-asteroid-4", "Core Asteroid 4", "Asteroid 4", "7fa7b7"),
            new("core-asteroid-5", "Core Asteroid 5", "Asteroid 5", "bc156b"),
            new("core-asteroid-6", "Core Asteroid 6", "Asteroid 6", "1f74b0")
        ]),
        new("tfa-asteroids", "The Force Awakens Asteroids",
        [
            new("tfa-asteroid-1", "TFA Asteroid 1", "TFA Asteroid 1", "ac1f52"),
            new("tfa-asteroid-2", "TFA Asteroid 2", "TFA Asteroid 2", "4e1f1e"),
            new("tfa-asteroid-3", "TFA Asteroid 3", "TFA Asteroid 3", "54eca6"),
            new("tfa-asteroid-4", "TFA Asteroid 4", "TFA Asteroid 4", "e22584"),
            new("tfa-asteroid-5", "TFA Asteroid 5", "TFA Asteroid 5", "62c2e4"),
            new("tfa-asteroid-6", "TFA Asteroid 6", "TFA Asteroid 6", "157bbd")
        ]),
        new("debris-clouds", "Debris Clouds",
        [
            new("debris-cloud-1", "Debris Cloud 1", "Debrisfield 1", "f2766c"),
            new("debris-cloud-2", "Debris Cloud 2", "Debrisfield 2", "fcf984"),
            new("debris-cloud-3", "Debris Cloud 3", "Debrisfield 3", "f43a6a"),
            new("debris-cloud-4", "Debris Cloud 4", "Debrisfield 4", "72ac5e"),
            new("debris-cloud-5", "Debris Cloud 5", "Debrisfield 5", "46114f"),
            new("debris-cloud-6", "Debris Cloud 6", "Debrisfield 6", "416398")
        ])
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R22 First Edition Obstacle Set Review");
        Console.WriteLine("=================================================================");
        Console.WriteLine();
        if (args.Length < 2) { ShowUsage(); return 1; }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            var inventoryPath = Path.GetFullPath(Option(args, "--inventory") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "gameplay-object-inventory", "first-edition-gameplay-objects.json"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "obstacle-set-review"));
            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "Unified 2.5 reference save");
            RequireFile(inventoryPath, "First Edition gameplay-object inventory");
            ValidateRequirements(inventoryPath);
            Directory.CreateDirectory(output);

            var root = JsonNode.Parse(File.ReadAllText(referenceSave))?.AsObject()
                ?? throw new InvalidDataException("Could not parse Unified 2.5 reference save.");
            var sourceObjects = root["ObjectStates"]?.AsArray()
                ?? throw new InvalidDataException("Reference save does not contain ObjectStates.");
            var rows = ResolveRows(sourceObjects);

            var savePath = Path.Combine(output, "first-edition-obstacle-set-review.json");
            var selectionsPath = Path.Combine(output, "first-edition-obstacle-set-selections.csv");
            var manifestPath = Path.Combine(output, "first-edition-obstacle-set-review-manifest.json");
            var reportPath = Path.Combine(output, "FIRST-EDITION-OBSTACLE-SET-REVIEW.md");
            var manifest = new ObstacleReviewManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Phase = "16E-R22",
                Policy = "Review only. No obstacle asset, mapping, Lua script, or gameplay state is approved, imported, or modified.",
                RepositoryRoot = Normalise(repository),
                InventoryPath = Normalise(inventoryPath),
                ReferenceSavePath = Normalise(referenceSave),
                ReferenceSaveSha256 = HashFile(referenceSave),
                SetCount = Sets.Length,
                PhysicalObstacleCount = rows.Count,
                AlternateStateCountExcluded = rows.Sum(row => row.SourceStateCount),
                Sets = Sets.Select(set => new ObstacleSetReview
                {
                    RequirementId = set.RequirementId,
                    Name = set.Name,
                    ExpectedPieceCount = 6,
                    Pieces = rows.Where(row => row.RequirementId == set.RequirementId).ToList()
                }).ToList(),
                Warnings =
                [
                    "Each source object contains standard and Pride alternate states. The review save displays only the standard First Edition-relevant form.",
                    "Object Lua is removed from the review clones. This step reviews physical assets, scale, mesh, collider, and texture only.",
                    "The four remaining optional gameplay-object definitions are outside this review and remain deferred."
                ]
            };

            File.WriteAllText(savePath, BuildSave(root, rows).ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteSelections(selectionsPath, rows);
            WriteReport(reportPath, manifest, savePath, selectionsPath, manifestPath);

            Console.WriteLine($"Repository:                   {repository}");
            Console.WriteLine($"Inventory:                    {inventoryPath}");
            Console.WriteLine($"Unified 2.5 reference save:   {referenceSave}");
            Console.WriteLine($"Obstacle sets:                {manifest.SetCount}");
            Console.WriteLine($"Physical obstacles:           {manifest.PhysicalObstacleCount}");
            Console.WriteLine($"Alternate states excluded:    {manifest.AlternateStateCountExcluded}");
            Console.WriteLine($"Warnings:                     {manifest.Warnings.Count}");
            Console.WriteLine();
            Console.WriteLine($"TTS review save: {savePath}");
            Console.WriteLine($"Selections:      {selectionsPath}");
            Console.WriteLine($"Manifest:        {manifestPath}");
            Console.WriteLine($"Report:          {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Obstacle review package prepared. No assets, mappings, Lua scripts or gameplay state were modified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Obstacle set review preparation failed: {exception.Message}");
            return 1;
        }
    }

    private static void ValidateRequirements(string inventoryPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
        var requirements = document.RootElement.GetProperty("requirements").EnumerateArray()
            .Select(item => Text(item, "id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = Sets.Where(set => !requirements.Contains(set.RequirementId)).Select(set => set.RequirementId).ToList();
        if (missing.Count > 0) throw new InvalidDataException($"Inventory is missing obstacle requirements: {string.Join(", ", missing)}.");
    }

    private static List<ObstaclePieceReview> ResolveRows(JsonArray sourceObjects)
    {
        var rows = new List<ObstaclePieceReview>();
        foreach (var set in Sets)
        foreach (var piece in set.Pieces)
        {
            var matches = sourceObjects.Select((node, index) => (node, index))
                .Where(item => item.node is JsonObject obj &&
                    string.Equals(obj["GUID"]?.GetValue<string>(), piece.SourceGuid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(obj["Nickname"]?.GetValue<string>(), piece.SourceNickname, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
                throw new InvalidDataException($"Expected one source object for {piece.Name} ({piece.SourceGuid}); found {matches.Count}.");
            var source = matches[0].node!.AsObject();
            var mesh = source["CustomMesh"]?.AsObject() ?? throw new InvalidDataException($"{piece.Name} has no CustomMesh.");
            var transform = source["Transform"]?.AsObject() ?? throw new InvalidDataException($"{piece.Name} has no Transform.");
            rows.Add(new ObstaclePieceReview
            {
                RequirementId = set.RequirementId,
                SetName = set.Name,
                PieceId = piece.Id,
                Name = piece.Name,
                SourcePath = $"ObjectStates[{matches[0].index}]",
                SourceGuid = piece.SourceGuid,
                SourceNickname = piece.SourceNickname,
                ObjectType = source["Name"]?.GetValue<string>() ?? string.Empty,
                MeshUrl = mesh["MeshURL"]?.GetValue<string>() ?? string.Empty,
                TextureUrl = mesh["DiffuseURL"]?.GetValue<string>() ?? string.Empty,
                ColliderUrl = mesh["ColliderURL"]?.GetValue<string>() ?? string.Empty,
                ScaleX = Number(transform, "scaleX"),
                ScaleY = Number(transform, "scaleY"),
                ScaleZ = Number(transform, "scaleZ"),
                SourceStateCount = (source["States"] as JsonObject)?.Count ?? 0,
                LuaPresentInSource = !string.IsNullOrWhiteSpace(source["LuaScript"]?.GetValue<string>()),
                SourceObjectSha256 = HashText(source.ToJsonString())
            });
        }
        return rows;
    }

    private static JsonObject BuildSave(JsonObject sourceRoot, List<ObstaclePieceReview> rows)
    {
        var save = sourceRoot.DeepClone().AsObject();
        var sourceObjects = sourceRoot["ObjectStates"]!.AsArray();
        var objects = new JsonArray();
        const float spacingX = 5.5f;
        var startX = -(5 * spacingX) / 2f;
        var rowZ = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["core-asteroids"] = 8f,
            ["tfa-asteroids"] = 0f,
            ["debris-clouds"] = -8f
        };

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var sourceIndex = int.Parse(row.SourcePath["ObjectStates[".Length..^1], CultureInfo.InvariantCulture);
            var clone = sourceObjects[sourceIndex]!.DeepClone().AsObject();
            var withinSet = index % 6;
            clone["GUID"] = $"0b{index + 1:x4}";
            clone["Nickname"] = row.Name;
            clone["Description"] = $"PHASE 16E-R22 REVIEW ONLY\nSet: {row.SetName}\nPiece: {withinSet + 1} of 6";
            clone["GMNotes"] = JsonSerializer.Serialize(new
            {
                phase = "16E-R22", reviewOnly = true, row.RequirementId, row.PieceId,
                row.SourcePath, row.SourceGuid, alternateStatesExcluded = row.SourceStateCount
            });
            clone.Remove("States");
            clone["LuaScript"] = string.Empty;
            clone["LuaScriptState"] = string.Empty;
            clone["XmlUI"] = string.Empty;
            clone["Locked"] = false;
            clone["DragSelectable"] = true;
            var transform = clone["Transform"]!.AsObject();
            transform["posX"] = startX + withinSet * spacingX;
            transform["posY"] = 1.15;
            transform["posZ"] = rowZ[row.RequirementId];
            transform["rotX"] = 0.0;
            transform["rotZ"] = 0.0;
            objects.Add(clone);
        }

        save["SaveName"] = "X-Wing Unified 1E - Phase 16E-R22 Obstacle Set Review";
        save["GameMode"] = string.Empty;
        save["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
        save["Note"] = "Review only. Top row: Core Set asteroids. Middle row: Force Awakens asteroids. Bottom row: debris clouds. Standard textures only; Pride alternates and gameplay Lua excluded.";
        save["Rules"] = string.Empty;
        save["XmlUI"] = string.Empty;
        save["LuaScript"] = string.Empty;
        save["LuaScriptState"] = string.Empty;
        save["ObjectStates"] = objects;
        return save;
    }

    private static void WriteSelections(string path, IEnumerable<ObstaclePieceReview> rows)
    {
        var csv = new StringBuilder();
        csv.AppendLine("RequirementId,SetName,PieceId,Name,SourceGuid,SourcePath,Decision,Notes");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", Csv(row.RequirementId), Csv(row.SetName), Csv(row.PieceId), Csv(row.Name),
                Csv(row.SourceGuid), Csv(row.SourcePath), "", ""));
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
    }

    private static void WriteReport(string path, ObstacleReviewManifest manifest, string savePath, string selectionsPath, string manifestPath)
    {
        var report = new StringBuilder();
        report.AppendLine("# First Edition Obstacle Set Review");
        report.AppendLine();
        report.AppendLine("> Review only. No obstacle has been imported or approved by this command.");
        report.AppendLine();
        report.AppendLine("## Layout");
        report.AppendLine();
        report.AppendLine("- Top row: six original Core Set asteroids.");
        report.AppendLine("- Middle row: six The Force Awakens Core Set asteroids.");
        report.AppendLine("- Bottom row: six debris clouds.");
        report.AppendLine();
        report.AppendLine("## Outputs");
        report.AppendLine();
        report.AppendLine($"- TTS review save: `{Normalise(savePath)}`");
        report.AppendLine($"- Selections CSV: `{Normalise(selectionsPath)}`");
        report.AppendLine($"- Review manifest: `{Normalise(manifestPath)}`");
        report.AppendLine();
        report.AppendLine("## Review criteria");
        report.AppendLine();
        report.AppendLine("Check all 18 pieces for physical size, silhouette, image quality, mesh thickness, collider fit, selection outline, flipping, and stacking. Record any individual exception even if the rest of its set is approved.");
        report.AppendLine();
        report.AppendLine("## Warnings");
        report.AppendLine();
        foreach (var warning in manifest.Warnings) report.AppendLine($"- {warning}");
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
    }

    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }
    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static double Number(JsonObject obj, string name) => obj[name]?.GetValue<double>() ?? 0.0;
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string Normalise(string path) => path.Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found: {path}", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: prepare-first-edition-obstacle-set-review <first-edition-repo-folder> <tts-reference-save.json> [--inventory <file>] [--output <folder>]");

    private sealed record ObstacleSetSeed(string RequirementId, string Name, ObstaclePieceSeed[] Pieces);
    private sealed record ObstaclePieceSeed(string Id, string Name, string SourceNickname, string SourceGuid);

    public sealed class ObstacleReviewManifest
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
        public string RepositoryRoot { get; set; } = string.Empty;
        public string InventoryPath { get; set; } = string.Empty;
        public string ReferenceSavePath { get; set; } = string.Empty;
        public string ReferenceSaveSha256 { get; set; } = string.Empty;
        public int SetCount { get; set; }
        public int PhysicalObstacleCount { get; set; }
        public int AlternateStateCountExcluded { get; set; }
        public List<ObstacleSetReview> Sets { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    public sealed class ObstacleSetReview
    {
        public string RequirementId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ExpectedPieceCount { get; set; }
        public List<ObstaclePieceReview> Pieces { get; set; } = [];
    }

    public sealed class ObstaclePieceReview
    {
        public string RequirementId { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string PieceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string SourceGuid { get; set; } = string.Empty;
        public string SourceNickname { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string MeshUrl { get; set; } = string.Empty;
        public string TextureUrl { get; set; } = string.Empty;
        public string ColliderUrl { get; set; } = string.Empty;
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public double ScaleZ { get; set; }
        public int SourceStateCount { get; set; }
        public bool LuaPresentInSource { get; set; }
        public string SourceObjectSha256 { get; set; } = string.Empty;
    }
}
