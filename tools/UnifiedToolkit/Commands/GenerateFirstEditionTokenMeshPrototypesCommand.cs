using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R7 generates review-only OBJ token meshes, simple colliders and a
/// TTS geometry save. Existing raster artwork and runtime Lua are not modified.
/// </summary>
public static class GenerateFirstEditionTokenMeshPrototypesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const float ModelThickness = 0.18f;
    private const float ReviewScale = 0.375f;

    private static readonly PrototypeSeed[] Seeds =
    [
        new("extruded-round", "Extruded round", "Token_Focus.png", "focus", Shape.Round),
        new("extruded-rounded-square", "Extruded rounded square", "Token-tractor.png", "tractor", Shape.RoundedSquare),
        new("new-chamfered-square", "Chamfered-square Ordnance", "Token-ordnance.png", "ordnance", Shape.ChamferedSquare),
        new("new-triangle", "Triangular Stress", "Stress.png", "stress", Shape.ChamferedTriangle)
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R7 Token Mesh Prototypes");
        Console.WriteLine("==================================================");
        Console.WriteLine();
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            var blueprintPath = Path.GetFullPath(Option(args, "--blueprint") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "token-construction-blueprint", "first-edition-token-construction-blueprint.json"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "token-mesh-prototypes"));
            var assetBaseUrl = (Option(args, "--asset-base-url") ??
                "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/").TrimEnd('/') + "/";
            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "TTS reference save");
            RequireFile(blueprintPath, "Phase 16E-R6 token blueprint");

            var meshRoot = Path.Combine(repository, "assets", "generated", "GameplayTokens", "review-meshes");
            Directory.CreateDirectory(meshRoot);
            Directory.CreateDirectory(output);
            using var blueprint = JsonDocument.Parse(File.ReadAllBytes(blueprintPath));
            var artwork = ReadArtwork(blueprint.RootElement);
            var warnings = new List<string>();
            var prototypes = new List<TokenMeshPrototype>();

            foreach (var seed in Seeds)
            {
                if (!artwork.TryGetValue(seed.TokenId, out var artworkPath) || string.IsNullOrWhiteSpace(artworkPath))
                {
                    warnings.Add($"{seed.Name}: selected artwork was not found in the R6 blueprint.");
                    continue;
                }
                var artworkFile = RepositoryFile(repository, artworkPath);
                if (!File.Exists(artworkFile))
                {
                    warnings.Add($"{seed.Name}: artwork does not exist locally: {artworkPath}");
                    continue;
                }

                var modelPoints = Points(seed.Geometry, false);
                var colliderPoints = Points(seed.Geometry, true);
                var safeId = seed.MeshFamilyId.Replace('-', '_');
                var modelPath = Path.Combine(meshRoot, safeId + ".obj");
                var colliderPath = Path.Combine(meshRoot, safeId + "_collider.obj");
                File.WriteAllText(modelPath, BuildTexturedPrism(seed.Name, modelPoints, ModelThickness), new UTF8Encoding(false));
                File.WriteAllText(colliderPath, BuildCollider(seed.Name + " collider", colliderPoints, ModelThickness), new UTF8Encoding(false));
                prototypes.Add(new TokenMeshPrototype
                {
                    MeshFamilyId = seed.MeshFamilyId,
                    Name = seed.Name,
                    TokenId = seed.TokenId,
                    Shape = seed.Geometry.ToString(),
                    Thickness = ModelThickness,
                    ReviewScale = ReviewScale,
                    VertexCount = modelPoints.Count * 6,
                    FaceArtworkPath = NormalisePath(artworkPath),
                    ModelPath = Relative(repository, modelPath),
                    ColliderPath = Relative(repository, colliderPath),
                    Status = "review-only"
                });
            }

            var savePath = Path.Combine(output, "first-edition-token-mesh-prototype-review.json");
            var manifestPath = Path.Combine(output, "first-edition-token-mesh-prototypes.json");
            var checklistPath = Path.Combine(output, "first-edition-token-mesh-prototype-review.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-TOKEN-MESH-PROTOTYPE-REVIEW.md");
            var manifest = new TokenMeshPrototypeManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Review-only generated geometry. No prototype is canonical or runtime-enabled.",
                RepositoryRoot = NormalisePath(repository),
                BlueprintPath = NormalisePath(blueprintPath),
                ReferenceSavePath = NormalisePath(referenceSave),
                AssetBaseUrl = assetBaseUrl,
                PrototypeCount = prototypes.Count,
                WarningCount = warnings.Count,
                Warnings = warnings,
                Prototypes = prototypes
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(savePath, BuildSave(referenceSave, manifest).ToJsonString(JsonOptions), new UTF8Encoding(false));
            WriteChecklist(checklistPath, prototypes);
            WriteReport(reportPath, manifest);

            Console.WriteLine($"Repository:               {repository}");
            Console.WriteLine($"Blueprint:                {blueprintPath}");
            Console.WriteLine($"Reference save:           {referenceSave}");
            Console.WriteLine($"Mesh prototypes:          {prototypes.Count}");
            Console.WriteLine($"OBJ model files:          {prototypes.Count}");
            Console.WriteLine($"OBJ collider files:       {prototypes.Count}");
            Console.WriteLine($"Images modified:          0");
            Console.WriteLine($"Lua scripts added:        0");
            Console.WriteLine($"Warnings:                 {warnings.Count}");
            Console.WriteLine();
            Console.WriteLine($"Generated meshes: {meshRoot}");
            Console.WriteLine($"TTS review save:  {savePath}");
            Console.WriteLine($"Manifest:         {manifestPath}");
            Console.WriteLine($"Checklist:        {checklistPath}");
            Console.WriteLine($"Report:           {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Prototype generation completed. Existing images, mappings, Lua scripts and gameplay state were not modified.");
            return warnings.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Token mesh prototype generation failed: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ReadArtwork(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryProperty(root, "Decisions", out var decisions) || decisions.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("R6 blueprint does not contain Decisions.");
        foreach (var item in decisions.EnumerateArray())
        {
            var id = String(item, "Id");
            var path = String(item, "SelectedArtworkPath");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(path)) result[id] = path;
        }
        return result;
    }

    private static List<Point> Points(Shape shape, bool collider) => shape switch
    {
        Shape.Round => Circle(collider ? 16 : 32),
        Shape.RoundedSquare => RoundedSquare(collider ? 2 : 5),
        Shape.ChamferedSquare =>
        [
            new(-0.68f, -1f), new(0.68f, -1f), new(1f, -0.68f), new(1f, 0.68f),
            new(0.68f, 1f), new(-0.68f, 1f), new(-1f, 0.68f), new(-1f, -0.68f)
        ],
        Shape.ChamferedTriangle =>
        [
            new(-0.82f, -0.68f), new(0.82f, -0.68f), new(0.95f, -0.48f),
            new(0.16f, 0.92f), new(-0.16f, 0.92f), new(-0.95f, -0.48f)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private static List<Point> Circle(int segments)
    {
        var points = new List<Point>();
        for (var index = 0; index < segments; index++)
        {
            var angle = -Math.PI / 2 + index * Math.PI * 2 / segments;
            points.Add(new Point((float)Math.Cos(angle), (float)Math.Sin(angle)));
        }
        return points;
    }

    private static List<Point> RoundedSquare(int segmentsPerCorner)
    {
        const float radius = 0.28f;
        const float centre = 1f - radius;
        var points = new List<Point>();
        foreach (var corner in new[] { (centre, -centre, -90d), (centre, centre, 0d), (-centre, centre, 90d), (-centre, -centre, 180d) })
        for (var index = 0; index <= segmentsPerCorner; index++)
        {
            var angle = (corner.Item3 + index * 90d / segmentsPerCorner) * Math.PI / 180d;
            points.Add(new Point(corner.Item1 + radius * (float)Math.Cos(angle), corner.Item2 + radius * (float)Math.Sin(angle)));
        }
        return points;
    }

    private static string BuildTexturedPrism(string name, IReadOnlyList<Point> points, float thickness)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# UnifiedToolkit Phase 16E-R7 review-only token mesh");
        builder.AppendLine("o " + SafeName(name));
        var half = thickness / 2f;
        foreach (var point in points) builder.AppendLine(FormattableString.Invariant($"v {point.X:0.######} {half:0.######} {point.Z:0.######}"));
        foreach (var point in points) builder.AppendLine(FormattableString.Invariant($"v {point.X:0.######} {-half:0.######} {point.Z:0.######}"));
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            foreach (var pair in new[] { points[index], points[next] })
            {
                builder.AppendLine(FormattableString.Invariant($"v {pair.X:0.######} {half:0.######} {pair.Z:0.######}"));
                builder.AppendLine(FormattableString.Invariant($"v {pair.X:0.######} {-half:0.######} {pair.Z:0.######}"));
            }
        }
        foreach (var point in points)
        {
            builder.AppendLine(FormattableString.Invariant($"vt {(point.X + 1f) / 2f:0.######} {(point.Z + 1f) / 2f:0.######}"));
        }
        foreach (var point in points)
        {
            builder.AppendLine(FormattableString.Invariant($"vt {(point.X + 1f) / 2f:0.######} {1f - (point.Z + 1f) / 2f:0.######}"));
        }
        for (var index = 0; index < points.Count * 4; index++) builder.AppendLine("vt 0.08 0.5");
        builder.AppendLine("vn 0 1 0");
        builder.AppendLine("vn 0 -1 0");
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            var current = points[index];
            var dx = next.X - current.X;
            var dz = next.Z - current.Z;
            var length = MathF.Sqrt(dx * dx + dz * dz);
            builder.AppendLine(FormattableString.Invariant($"vn {dz / length:0.######} 0 {-dx / length:0.######}"));
        }
        for (var index = 1; index < points.Count - 1; index++) builder.AppendLine($"f 1/1/1 {index + 2}/{index + 2}/1 {index + 1}/{index + 1}/1");
        var bottom = points.Count;
        for (var index = 1; index < points.Count - 1; index++) builder.AppendLine($"f {bottom + 1}/{bottom + 1}/2 {bottom + index + 1}/{bottom + index + 1}/2 {bottom + index + 2}/{bottom + index + 2}/2");
        var sideVertex = points.Count * 2 + 1;
        var sideUv = points.Count * 2 + 1;
        for (var index = 0; index < points.Count; index++)
        {
            var normal = index + 3;
            builder.AppendLine($"f {sideVertex}/{sideUv}/{normal} {sideVertex + 2}/{sideUv + 2}/{normal} {sideVertex + 3}/{sideUv + 3}/{normal} {sideVertex + 1}/{sideUv + 1}/{normal}");
            sideVertex += 4;
            sideUv += 4;
        }
        return builder.ToString();
    }

    private static string BuildCollider(string name, IReadOnlyList<Point> points, float thickness)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# UnifiedToolkit Phase 16E-R7 convex review collider");
        builder.AppendLine("o " + SafeName(name));
        var half = thickness / 2f;
        foreach (var point in points) builder.AppendLine(FormattableString.Invariant($"v {point.X:0.######} {half:0.######} {point.Z:0.######}"));
        foreach (var point in points) builder.AppendLine(FormattableString.Invariant($"v {point.X:0.######} {-half:0.######} {point.Z:0.######}"));
        for (var index = 1; index < points.Count - 1; index++) builder.AppendLine($"f 1 {index + 2} {index + 1}");
        var bottom = points.Count;
        for (var index = 1; index < points.Count - 1; index++) builder.AppendLine($"f {bottom + 1} {bottom + index + 1} {bottom + index + 2}");
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            builder.AppendLine($"f {index + 1} {next + 1} {bottom + next + 1} {bottom + index + 1}");
        }
        return builder.ToString();
    }

    private static JsonObject BuildSave(string referenceSave, TokenMeshPrototypeManifest manifest)
    {
        var objects = new JsonArray();
        var counter = 1;
        var startX = -((manifest.Prototypes.Count - 1) * 5f) / 2f;
        for (var index = 0; index < manifest.Prototypes.Count; index++)
        {
            var item = manifest.Prototypes[index];
            var x = startX + index * 5f;
            objects.Add(Label(Guid(counter++), item.Name, "Geometry-only prototype", x, 4f));
            objects.Add(Model(Guid(counter++), item, manifest.AssetBaseUrl, x, 0f));
        }
        var envelope = JsonNode.Parse(File.ReadAllText(referenceSave))?.AsObject()
            ?? throw new InvalidDataException($"Could not parse TTS reference save: {referenceSave}");
        envelope["SaveName"] = "X-Wing Unified 1E - Phase 16E-R7 Token Mesh Prototypes";
        envelope["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
        envelope["Note"] = "Review geometry, thickness, silhouette, flipping and collision only. Existing artwork is unmodified and no prototype is approved.";
        envelope["Rules"] = string.Empty;
        envelope["XmlUI"] = string.Empty;
        envelope["LuaScript"] = string.Empty;
        envelope["LuaScriptState"] = string.Empty;
        envelope["ObjectStates"] = objects;
        return envelope;
    }

    private static JsonObject Model(string guid, TokenMeshPrototype item, string baseUrl, float x, float z) => new()
    {
        ["GUID"] = guid, ["Name"] = "Custom_Model",
        ["Transform"] = Transform(x, 1.2f, z, item.ReviewScale),
        ["Nickname"] = item.Name,
        ["Description"] = $"Model: {item.ModelPath}\nCollider: {item.ColliderPath}\nArtwork unchanged: {item.FaceArtworkPath}",
        ["GMNotes"] = "Phase 16E-R7 review-only geometry prototype",
        ["AltLookAngle"] = Vector(), ["ColorDiffuse"] = Color(1f, 1f, 1f),
        ["Locked"] = false, ["Grid"] = true, ["Snap"] = true, ["IgnoreFoW"] = false,
        ["MeasureMovement"] = false, ["DragSelectable"] = true, ["Autoraise"] = true,
        ["Sticky"] = true, ["Tooltip"] = true, ["GridProjection"] = false,
        ["HideWhenFaceDown"] = false, ["Hands"] = false,
        ["CustomMesh"] = new JsonObject
        {
            ["MeshURL"] = AssetUrl(baseUrl, item.ModelPath),
            ["DiffuseURL"] = AssetUrl(baseUrl, item.FaceArtworkPath),
            ["NormalURL"] = string.Empty,
            ["ColliderURL"] = AssetUrl(baseUrl, item.ColliderPath),
            ["Convex"] = true, ["MaterialIndex"] = 3, ["TypeIndex"] = 0, ["CastShadows"] = true
        },
        ["LuaScript"] = string.Empty, ["LuaScriptState"] = string.Empty, ["XmlUI"] = string.Empty
    };

    private static JsonObject Label(string guid, string title, string detail, float x, float z) => new()
    {
        ["GUID"] = guid, ["Name"] = "Notecard", ["Transform"] = Transform(x, 1f, z, 1.2f),
        ["Nickname"] = title, ["Description"] = detail, ["GMNotes"] = "Phase 16E-R7 label",
        ["AltLookAngle"] = Vector(), ["ColorDiffuse"] = Color(0.25f, 0.35f, 0.55f),
        ["Locked"] = true, ["Grid"] = true, ["Snap"] = true, ["IgnoreFoW"] = false,
        ["MeasureMovement"] = false, ["DragSelectable"] = true, ["Autoraise"] = true,
        ["Sticky"] = true, ["Tooltip"] = true, ["GridProjection"] = false,
        ["HideWhenFaceDown"] = false, ["Hands"] = false,
        ["Memo"] = title + "\n\n" + detail, ["LuaScript"] = string.Empty,
        ["LuaScriptState"] = string.Empty, ["XmlUI"] = string.Empty
    };

    private static JsonObject Transform(float x, float y, float z, float scale) => new()
    {
        ["posX"] = x, ["posY"] = y, ["posZ"] = z,
        ["rotX"] = 0f, ["rotY"] = 180f, ["rotZ"] = 0f,
        ["scaleX"] = scale, ["scaleY"] = scale, ["scaleZ"] = scale
    };
    private static JsonObject Vector() => new() { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f };
    private static JsonObject Color(float r, float g, float b) => new() { ["r"] = r, ["g"] = g, ["b"] = b };

    private static void WriteChecklist(string path, IEnumerable<TokenMeshPrototype> items)
    {
        var lines = new List<string> { "MeshFamilyId,Name,TokenId,Shape,Thickness,ReviewScale,ModelPath,ColliderPath,ArtworkPath,Decision,Notes" };
        lines.AddRange(items.Select(item => Csv(item.MeshFamilyId, item.Name, item.TokenId, item.Shape, item.Thickness.ToString("0.###", CultureInfo.InvariantCulture), item.ReviewScale.ToString("0.###", CultureInfo.InvariantCulture), item.ModelPath, item.ColliderPath, item.FaceArtworkPath, "", "")));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(string path, TokenMeshPrototypeManifest manifest)
    {
        var lines = new List<string>
        {
            "# Phase 16E-R7 First Edition Token Mesh Prototype Review", "",
            "Geometry review only. Existing raster artwork was not changed and no Lua was added.", "",
            $"- Prototypes: **{manifest.PrototypeCount}**", $"- Warnings: **{manifest.WarningCount}**", "",
            "Review silhouette, thickness, face orientation, edge solidity, flipping, stacking and collider fit.", "",
            "The generated OBJ files must be pushed before the raw GitHub URLs in the TTS review save can load.", "",
            "## Prototypes", ""
        };
        lines.AddRange(manifest.Prototypes.Select(item => $"- **{item.Name}** — `{item.ModelPath}`"));
        lines.AddRange(["", "## Warnings", ""]);
        lines.AddRange(manifest.Warnings.Count == 0 ? ["- None."] : manifest.Warnings.Select(value => "- " + value));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string AssetUrl(string root, string path) => root + string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    private static string RepositoryFile(string root, string path) => Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string root, string path) => NormalisePath(Path.GetRelativePath(root, path));
    private static string NormalisePath(string value) => value.Replace('\\', '/');
    private static string SafeName(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    private static string Csv(params string[] values) => string.Join(',', values.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));
    private static string Guid(int value) => value.ToString("x6", CultureInfo.InvariantCulture)[^6..];
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: generate-first-edition-token-mesh-prototypes <first-edition-repo-folder> <tts-reference-save.json> [--blueprint <file>] [--asset-base-url <url>] [--output <folder>]");
    private static bool TryProperty(JsonElement item, string name, out JsonElement value) => item.TryGetProperty(name, out value) || item.TryGetProperty(char.ToLowerInvariant(name[0]) + name[1..], out value);
    private static string String(JsonElement item, string name) => TryProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private enum Shape { Round, RoundedSquare, ChamferedSquare, ChamferedTriangle }
    private sealed record PrototypeSeed(string MeshFamilyId, string Name, string PreferredArtworkFilename, string TokenId, Shape Geometry);
    private sealed record Point(float X, float Z);
}

public sealed class TokenMeshPrototypeManifest
{
    public string SchemaVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Policy { get; init; } = "";
    public string RepositoryRoot { get; init; } = "";
    public string BlueprintPath { get; init; } = "";
    public string ReferenceSavePath { get; init; } = "";
    public string AssetBaseUrl { get; init; } = "";
    public int PrototypeCount { get; init; }
    public int WarningCount { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<TokenMeshPrototype> Prototypes { get; init; } = [];
}

public sealed class TokenMeshPrototype
{
    public string MeshFamilyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string TokenId { get; init; } = "";
    public string Shape { get; init; } = "";
    public float Thickness { get; init; }
    public float ReviewScale { get; init; }
    public int VertexCount { get; init; }
    public string FaceArtworkPath { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string ColliderPath { get; init; } = "";
    public string Status { get; init; } = "";
}
