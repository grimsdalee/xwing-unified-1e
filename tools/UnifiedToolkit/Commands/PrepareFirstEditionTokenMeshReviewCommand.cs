using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Builds a review-only TTS save that applies existing First Edition candidate
/// artwork to plausible Unified 2.5 token meshes. It imports and approves nothing.
/// </summary>
public static class PrepareFirstEditionTokenMeshReviewCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, MeshDefinition> Meshes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["round"] = new("Round", "assets/Items/tokens/round/RoundToken.obj", "", 0.375f),
        ["rounded-square"] = new("Rounded square", "assets/Items/tokens/squared/RoundSquared.obj", "", 0.375f),
        ["clipped-square"] = new("Clipped square (Target Lock)", "assets/Items/tokens/squared/TL.obj", "", 0.375f),
        ["hexagon"] = new("Hexagonal", "assets/Items/tokens/squared/Hexagon.obj", "assets/Items/tokens/squared/Hexagon-collider.obj", 0.375f),
        ["reinforce"] = new("Reinforce", "assets/Items/tokens/new/reinforce-model.obj", "", 0.375f),
        ["shield"] = new("Shield sculpted", "assets/Items/tokens/shields/shield-model.obj", "assets/Items/tokens/shields/shield-collider.obj", 0.375f)
    };

    private static readonly ReviewDefinition[] Reviews =
    [
        new("ordnance", "Ordnance", ["clipped-square", "rounded-square", "hexagon"]),
        new("weapons-disabled", "Weapons disabled", ["round", "rounded-square", "clipped-square"]),
        new("jam", "Jam", ["round", "rounded-square", "clipped-square"]),
        new("cloak", "Cloak", ["rounded-square", "round", "clipped-square"]),
        new("ion", "Ion", ["rounded-square", "round", "clipped-square"]),
        new("stress", "Stress", ["rounded-square", "round", "clipped-square"]),
        new("tractor", "Tractor", ["round", "rounded-square", "clipped-square"]),
        new("reinforce", "Reinforce", ["reinforce", "round", "clipped-square"]),
        new("energy", "Energy", ["shield", "round", "clipped-square"]),
        new("shield", "Shield", ["shield", "round", "clipped-square"]),
        new("focus", "Focus", ["round", "rounded-square"]),
        new("evade", "Evade", ["round", "rounded-square"]),
        new("target-lock", "Target Lock", ["clipped-square"])
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R5 First Edition Token Mesh Review");
        Console.WriteLine("===========================================================");
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
            var inventoryPath = Path.GetFullPath(Option(args, "--inventory") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "gameplay-object-inventory", "first-edition-gameplay-objects.json"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "token-mesh-review"));
            var assetBaseUrl = (Option(args, "--asset-base-url") ??
                "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/").TrimEnd('/') + "/";

            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "TTS reference save");
            RequireFile(inventoryPath, "First Edition gameplay-object inventory");
            Directory.CreateDirectory(output);

            using var inventory = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
            var requirements = ReadRequirements(inventory.RootElement);
            var rows = new List<MeshReviewRow>();
            var warnings = new List<string>();

            foreach (var review in Reviews)
            {
                if (!requirements.TryGetValue(review.RequirementId, out var requirement))
                {
                    warnings.Add($"Requirement '{review.RequirementId}' is missing from the inventory.");
                    continue;
                }

                var candidate = ChooseCandidate(repository, requirement, review.RequirementId);
                if (candidate is null)
                {
                    warnings.Add($"{review.Name} has no locally available raster candidate.");
                    continue;
                }

                var availableMeshes = review.MeshIds
                    .Select(id => Meshes[id])
                    .Where(mesh => MeshFilesAvailable(repository, mesh, warnings))
                    .ToList();
                if (availableMeshes.Count == 0)
                {
                    warnings.Add($"{review.Name} has no locally available mesh options.");
                    continue;
                }

                rows.Add(new MeshReviewRow
                {
                    RequirementId = review.RequirementId,
                    RequirementName = review.Name,
                    Artwork = candidate,
                    ArtworkUrl = AssetUrl(assetBaseUrl, candidate.RepositoryPath),
                    Meshes = availableMeshes
                });
            }

            var savePath = Path.Combine(output, "first-edition-token-mesh-review.json");
            var manifestPath = Path.Combine(output, "first-edition-token-mesh-review-manifest.json");
            var selectionsPath = Path.Combine(output, "first-edition-token-mesh-selections.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-TOKEN-MESH-REVIEW.md");
            var manifest = new MeshReviewManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Review only. No artwork or mesh is approved, imported, converted or enabled.",
                RepositoryRoot = NormalisePath(repository),
                InventoryPath = NormalisePath(inventoryPath),
                ReferenceSavePath = NormalisePath(referenceSave),
                AssetBaseUrl = assetBaseUrl,
                RowCount = rows.Count,
                ComparisonObjectCount = rows.Sum(row => row.Meshes.Count),
                WarningCount = warnings.Count,
                Warnings = warnings,
                Rows = rows
            };

            File.WriteAllText(savePath, BuildSave(referenceSave, manifest).ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteSelections(selectionsPath, rows);
            WriteReport(reportPath, manifest, savePath, selectionsPath);

            Console.WriteLine($"Repository:                  {repository}");
            Console.WriteLine($"Inventory:                   {inventoryPath}");
            Console.WriteLine($"Reference save:              {referenceSave}");
            Console.WriteLine($"Token types:                 {manifest.RowCount}");
            Console.WriteLine($"Mesh comparison objects:     {manifest.ComparisonObjectCount}");
            Console.WriteLine($"Warnings:                    {manifest.WarningCount}");
            Console.WriteLine();
            Console.WriteLine($"TTS review save: {savePath}");
            Console.WriteLine($"Selections:      {selectionsPath}");
            Console.WriteLine($"Manifest:        {manifestPath}");
            Console.WriteLine($"Report:          {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Review package prepared. No assets, mappings, Lua scripts or gameplay state were modified.");
            return warnings.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Token mesh review preparation failed: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, InventoryRequirement> ReadRequirements(JsonElement root)
    {
        if (!TryProperty(root, "requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Gameplay-object inventory does not contain a requirements array.");
        var result = new Dictionary<string, InventoryRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in requirements.EnumerateArray())
        {
            var id = String(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var requirement = new InventoryRequirement { Id = id, Name = String(item, "name") };
            if (TryProperty(item, "candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
            foreach (var candidate in candidates.EnumerateArray())
            {
                requirement.Candidates.Add(new TokenMeshArtworkCandidate
                {
                    Source = String(candidate, "source"),
                    NameEvidence = String(candidate, "nameEvidence"),
                    RepositoryPath = String(candidate, "repositoryPath"),
                    Extension = String(candidate, "extension"),
                    SizeBytes = Long(candidate, "sizeBytes"),
                    Sha256 = String(candidate, "sha256")
                });
            }
            result[id] = requirement;
        }
        return result;
    }

    private static TokenMeshArtworkCandidate? ChooseCandidate(string repository, InventoryRequirement requirement, string requirementId)
    {
        var eligible = requirement.Candidates
            .Where(candidate => IsRaster(candidate.Extension))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.RepositoryPath))
            .Where(candidate => File.Exists(Path.Combine(repository, candidate.RepositoryPath.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        if (requirementId.Equals("ordnance", StringComparison.OrdinalIgnoreCase))
        {
            var vassal = eligible.FirstOrDefault(candidate =>
                candidate.Source.Equals("xwvassal", StringComparison.OrdinalIgnoreCase) &&
                candidate.RepositoryPath.EndsWith("Token-ordnance.png", StringComparison.OrdinalIgnoreCase));
            if (vassal is not null) return vassal;
        }
        if (requirementId.Equals("target-lock", StringComparison.OrdinalIgnoreCase))
        {
            var approvedUnified25 = eligible.FirstOrDefault(candidate =>
                candidate.Source.Equals("unified25", StringComparison.OrdinalIgnoreCase) &&
                candidate.RepositoryPath.EndsWith("/TL.png", StringComparison.OrdinalIgnoreCase));
            if (approvedUnified25 is not null) return approvedUnified25;
        }

        return eligible
            .OrderBy(candidate => CandidateRank(requirementId, candidate))
            .ThenBy(candidate => SourceRank(candidate.Source))
            .ThenByDescending(candidate => candidate.SizeBytes)
            .ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static JsonObject BuildSave(string referenceSave, MeshReviewManifest manifest)
    {
        const float rowSpacing = 4.2f;
        const float columnSpacing = 4.0f;
        var objects = new JsonArray();
        var counter = 1;
        var startZ = ((manifest.Rows.Count - 1) * rowSpacing) / 2f;

        for (var rowIndex = 0; rowIndex < manifest.Rows.Count; rowIndex++)
        {
            var row = manifest.Rows[rowIndex];
            var z = startZ - rowIndex * rowSpacing;
            var startX = -((row.Meshes.Count - 1) * columnSpacing) / 2f;
            objects.Add(Label(Guid(counter++), row.RequirementName,
                $"Artwork: {row.Artwork.RepositoryPath}", startX - 6f, z));

            for (var meshIndex = 0; meshIndex < row.Meshes.Count; meshIndex++)
            {
                var mesh = row.Meshes[meshIndex];
                objects.Add(Model(Guid(counter++), row, mesh, manifest.AssetBaseUrl, startX + meshIndex * columnSpacing, z));
            }
        }

        var envelope = JsonNode.Parse(File.ReadAllText(referenceSave))?.AsObject()
            ?? throw new InvalidDataException($"Could not parse TTS reference save: {referenceSave}");
        envelope["SaveName"] = "X-Wing Unified 1E - Phase 16E-R5 Token Mesh Review";
        envelope["GameMode"] = string.Empty;
        envelope["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
        envelope["Note"] = "Review only. Compare visible silhouette, UV fit, edge shape, selection outline, stacking and collision. No option is approved by this save.";
        envelope["Rules"] = string.Empty;
        envelope["XmlUI"] = string.Empty;
        envelope["LuaScript"] = string.Empty;
        envelope["LuaScriptState"] = string.Empty;
        envelope["ObjectStates"] = objects;
        return envelope;
    }

    private static JsonObject Model(string guid, MeshReviewRow row, MeshDefinition mesh, string assetBaseUrl, float x, float z)
    {
        var notes = JsonSerializer.Serialize(new
        {
            phase = "16E-R5",
            requirementId = row.RequirementId,
            artwork = row.Artwork.RepositoryPath,
            mesh = mesh.MeshPath,
            decision = string.Empty
        });
        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Model",
            ["Transform"] = Transform(x, 1.2f, z, mesh.Scale),
            ["Nickname"] = $"{row.RequirementName} — {mesh.Name}",
            ["Description"] = $"Artwork: {row.Artwork.RepositoryPath}\nMesh: {mesh.MeshPath}\nReview silhouette, UV fit, transparent edges and physical boundary.",
            ["GMNotes"] = notes,
            ["AltLookAngle"] = Vector(),
            ["ColorDiffuse"] = Color(1f, 1f, 1f),
            ["LayoutGroupSortIndex"] = 0,
            ["Value"] = 0,
            ["Locked"] = false,
            ["Grid"] = true,
            ["Snap"] = true,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = true,
            ["Autoraise"] = true,
            ["Sticky"] = true,
            ["Tooltip"] = true,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomMesh"] = new JsonObject
            {
                ["MeshURL"] = AssetUrl(assetBaseUrl, "assets/source/unified25/" + mesh.MeshPath),
                ["DiffuseURL"] = row.ArtworkUrl,
                ["NormalURL"] = string.Empty,
                ["ColliderURL"] = string.IsNullOrWhiteSpace(mesh.ColliderPath) ? string.Empty : AssetUrl(assetBaseUrl, "assets/source/unified25/" + mesh.ColliderPath),
                ["Convex"] = true,
                ["MaterialIndex"] = 3,
                ["TypeIndex"] = 0,
                ["CastShadows"] = true
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static JsonObject Label(string guid, string title, string detail, float x, float z) => new()
    {
        ["GUID"] = guid,
        ["Name"] = "Notecard",
        ["Transform"] = Transform(x, 1f, z, 1.4f),
        ["Nickname"] = title,
        ["Description"] = detail,
        ["GMNotes"] = "Phase 16E-R5 mesh review row label",
        ["AltLookAngle"] = Vector(),
        ["ColorDiffuse"] = Color(0.25f, 0.35f, 0.55f),
        ["Locked"] = true,
        ["Grid"] = true,
        ["Snap"] = true,
        ["IgnoreFoW"] = false,
        ["MeasureMovement"] = false,
        ["DragSelectable"] = true,
        ["Autoraise"] = true,
        ["Sticky"] = true,
        ["Tooltip"] = true,
        ["GridProjection"] = false,
        ["HideWhenFaceDown"] = false,
        ["Hands"] = false,
        ["Memo"] = $"{title}\n\n{detail}\n\nReview only; this label approves nothing.",
        ["LuaScript"] = string.Empty,
        ["LuaScriptState"] = string.Empty,
        ["XmlUI"] = string.Empty
    };

    private static JsonObject Transform(float x, float y, float z, float scale) => new()
    {
        ["posX"] = x, ["posY"] = y, ["posZ"] = z,
        ["rotX"] = 0f, ["rotY"] = 180f, ["rotZ"] = 0f,
        ["scaleX"] = scale, ["scaleY"] = scale, ["scaleZ"] = scale
    };
    private static JsonObject Vector() => new() { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f };
    private static JsonObject Color(float r, float g, float b) => new() { ["r"] = r, ["g"] = g, ["b"] = b };

    private static void WriteSelections(string path, IEnumerable<MeshReviewRow> rows)
    {
        var lines = new List<string> { "RequirementId,RequirementName,ArtworkPath,MeshName,MeshPath,Decision,Notes" };
        foreach (var row in rows)
        foreach (var mesh in row.Meshes)
            lines.Add(string.Join(',', new[] { Quote(row.RequirementId), Quote(row.RequirementName), Quote(row.Artwork.RepositoryPath), Quote(mesh.Name), Quote(mesh.MeshPath), "\"\"", "\"\"" }));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(string path, MeshReviewManifest manifest, string savePath, string selectionsPath)
    {
        var lines = new List<string>
        {
            "# Phase 16E-R5 First Edition Token Mesh Review", "",
            $"- Token types: **{manifest.RowCount}**",
            $"- Mesh comparison objects: **{manifest.ComparisonObjectCount}**",
            $"- Warnings: **{manifest.WarningCount}**", "",
            "This is a geometry and UV compatibility review. It approves and imports nothing.", "",
            $"- TTS review save: `{NormalisePath(savePath)}`",
            $"- Editable selections: `{NormalisePath(selectionsPath)}`", "",
            "Check visible silhouette, artwork distortion, transparent corners, selection outline, stacking and collision. Use `approve`, `reject`, or `defer` in the Decision column.", "",
            "## Rows", ""
        };
        lines.AddRange(manifest.Rows.Select(row => $"- **{row.RequirementName}** — {row.Meshes.Count} meshes using `{row.Artwork.RepositoryPath}`"));
        lines.AddRange(["", "## Warnings", ""]);
        lines.AddRange(manifest.Warnings.Count == 0 ? ["- None."] : manifest.Warnings.Select(value => "- " + value));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static int CandidateRank(string requirementId, TokenMeshArtworkCandidate candidate)
    {
        var target = Normalise(requirementId);
        var evidence = Normalise(candidate.NameEvidence);
        var path = candidate.RepositoryPath.Replace('\\', '/').ToLowerInvariant();
        var score = evidence == "token" + target || evidence == target + "token" ? 0 : evidence.Contains(target) ? 20 : 60;
        if (evidence.Contains("2e") || evidence.StartsWith("u2e")) score += 200;
        if (path.Contains("/upgrade-cards/") || path.Contains("/pilot-cards/") || path.Contains("/reference-cards/")) score += 150;
        return score;
    }
    private static int SourceRank(string source) => source.ToLowerInvariant() switch { "unified1e" => 0, "xwvassal" => 1, "legacy1e-sorted" => 2, "legacy1e" => 3, "unified25" => 4, _ => 5 };
    private static bool MeshFilesAvailable(string repository, MeshDefinition mesh, List<string> warnings)
    {
        var root = Path.Combine(repository, "assets", "source", "unified25");
        var meshFile = Path.Combine(root, mesh.MeshPath.Replace('/', Path.DirectorySeparatorChar));
        var colliderFile = string.IsNullOrWhiteSpace(mesh.ColliderPath) ? null : Path.Combine(root, mesh.ColliderPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(meshFile))
        {
            warnings.Add($"Mesh option '{mesh.Name}' is unavailable: {NormalisePath(Path.GetRelativePath(repository, meshFile))}");
            return false;
        }
        if (colliderFile is not null && !File.Exists(colliderFile))
        {
            warnings.Add($"Collider for mesh option '{mesh.Name}' is unavailable: {NormalisePath(Path.GetRelativePath(repository, colliderFile))}");
            return false;
        }
        return true;
    }
    private static bool IsRaster(string extension) => extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string AssetUrl(string root, string path) => root + string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    private static string Guid(int value) => value.ToString("x6", CultureInfo.InvariantCulture)[^6..];
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string NormalisePath(string value) => value.Replace('\\', '/');
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: prepare-first-edition-token-mesh-review <first-edition-repo-folder> <tts-reference-save.json> [--inventory <file>] [--asset-base-url <url>] [--output <folder>]");
    private static bool TryProperty(JsonElement item, string name, out JsonElement value) => item.TryGetProperty(name, out value) || item.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    private static string String(JsonElement item, string name) => TryProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Long(JsonElement item, string name) => TryProperty(item, name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private sealed record ReviewDefinition(string RequirementId, string Name, string[] MeshIds);
    public sealed record MeshDefinition(string Name, string MeshPath, string ColliderPath, float Scale);
    private sealed class InventoryRequirement { public string Id { get; init; } = ""; public string Name { get; init; } = ""; public List<TokenMeshArtworkCandidate> Candidates { get; } = []; }
}

public sealed class MeshReviewManifest
{
    public string SchemaVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Policy { get; init; } = "";
    public string RepositoryRoot { get; init; } = "";
    public string InventoryPath { get; init; } = "";
    public string ReferenceSavePath { get; init; } = "";
    public string AssetBaseUrl { get; init; } = "";
    public int RowCount { get; init; }
    public int ComparisonObjectCount { get; init; }
    public int WarningCount { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<MeshReviewRow> Rows { get; init; } = [];
}

public sealed class MeshReviewRow
{
    public string RequirementId { get; init; } = "";
    public string RequirementName { get; init; } = "";
    public TokenMeshArtworkCandidate Artwork { get; init; } = new();
    public string ArtworkUrl { get; init; } = "";
    public List<PrepareFirstEditionTokenMeshReviewCommand.MeshDefinition> Meshes { get; init; } = [];
}

public sealed class TokenMeshArtworkCandidate
{
    public string Source { get; init; } = "";
    public string NameEvidence { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
}
