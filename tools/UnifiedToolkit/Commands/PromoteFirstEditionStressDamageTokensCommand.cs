using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R11 promotes the approved high-resolution Stress token and the
/// approved two-sided mission Damage token. Damage reuses the Critical Hit
/// geometry only as a validated construction reference; Critical Hit itself
/// remains unchanged. No runtime Lua is introduced.
/// </summary>
public static class PromoteFirstEditionStressDamageTokensCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R11 Stress and Mission Damage Token Promotion");
        Console.WriteLine("=======================================================================");
        Console.WriteLine();

        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            RequireDirectory(repository, "Repository");

            var canonicalStressMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/stress.obj");
            var canonicalStressFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/stress.png");
            var reviewStressMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/stress_test.obj");
            var reviewStressFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/stress_test_v2.png");

            var criticalHitMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/critical-hit.obj");
            var reviewDamageMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/damage_test.obj");
            var reviewDamageFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/damage_test.png");
            var canonicalDamageMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/damage.obj");
            var canonicalDamageFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/damage.png");

            var coreManifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/core-gameplay-tokens.json");
            var missionManifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/mission-gameplay-tokens.json");

            foreach (var required in new[]
            {
                canonicalStressMesh, canonicalStressFace, reviewStressMesh, reviewStressFace,
                criticalHitMesh, reviewDamageMesh, reviewDamageFace, coreManifestPath
            })
                RequireFile(required, "Required promotion input");

            var oldStressMesh = InspectObj(canonicalStressMesh);
            var newStressMesh = InspectObj(reviewStressMesh);
            var oldStressFace = InspectImage(canonicalStressFace);
            var newStressFace = InspectImage(reviewStressFace);
            ValidateMesh("Stress", oldStressMesh, newStressMesh);
            ValidateSquareFace("Stress", oldStressFace, newStressFace);

            var criticalInspection = InspectObj(criticalHitMesh);
            var damageInspection = InspectObj(reviewDamageMesh);
            var damageFaceInspection = InspectImage(reviewDamageFace);
            ValidateMesh("Damage", criticalInspection, damageInspection);
            if (damageFaceInspection.Width != damageFaceInspection.Height * 2)
                throw new InvalidDataException($"Damage texture must be a 2:1 two-face sheet; found {damageFaceInspection.Width}x{damageFaceInspection.Height}.");

            var criticalHashBefore = HashFile(criticalHitMesh);
            File.Copy(reviewStressMesh, canonicalStressMesh, true);
            File.Copy(reviewStressFace, canonicalStressFace, true);
            File.Copy(reviewDamageMesh, canonicalDamageMesh, true);
            File.Copy(reviewDamageFace, canonicalDamageFace, true);
            if (HashFile(criticalHitMesh) != criticalHashBefore)
                throw new InvalidOperationException("Critical Hit mesh changed during Damage promotion.");

            var stressMeshHash = HashFile(canonicalStressMesh);
            var stressFaceHash = HashFile(canonicalStressFace);
            var damageMeshHash = HashFile(canonicalDamageMesh);
            var damageFaceHash = HashFile(canonicalDamageFace);

            UpdateStressManifest(
                coreManifestPath, repository,
                canonicalStressMesh, canonicalStressFace,
                reviewStressMesh, reviewStressFace,
                newStressMesh, newStressFace,
                stressMeshHash, stressFaceHash);
            WriteMissionManifest(
                missionManifestPath, repository,
                canonicalDamageMesh, canonicalDamageFace,
                reviewDamageMesh, reviewDamageFace,
                criticalHitMesh, damageInspection, damageFaceInspection,
                damageMeshHash, damageFaceHash);

            var refreshKnowledgeBase = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refreshKnowledgeBase)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                  {repository}");
            Console.WriteLine($"Stress canonical mesh:       {canonicalStressMesh}");
            Console.WriteLine($"Stress canonical face:       {canonicalStressFace}");
            Console.WriteLine($"Damage canonical mesh:       {canonicalDamageMesh}");
            Console.WriteLine($"Damage canonical face:       {canonicalDamageFace}");
            Console.WriteLine($"Stress texture:              {newStressFace.Width} x {newStressFace.Height}");
            Console.WriteLine($"Damage texture:              {damageFaceInspection.Width} x {damageFaceInspection.Height}");
            Console.WriteLine($"Stress mesh SHA-256:         {stressMeshHash}");
            Console.WriteLine($"Stress face SHA-256:         {stressFaceHash}");
            Console.WriteLine($"Damage mesh SHA-256:         {damageMeshHash}");
            Console.WriteLine($"Damage face SHA-256:         {damageFaceHash}");
            Console.WriteLine($"Critical Hit mesh intact:    {HashFile(criticalHitMesh) == criticalHashBefore}");
            Console.WriteLine($"Core manifest:               {coreManifestPath}");
            Console.WriteLine($"Mission-token manifest:      {missionManifestPath}");
            Console.WriteLine($"Knowledge base refresh:      {(refreshKnowledgeBase ? "Yes" : "No")}");
            Console.WriteLine($"Images modified:             0");
            Console.WriteLine($"Lua scripts modified:        0");
            Console.WriteLine();
            Console.WriteLine("Stress and mission Damage tokens promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Stress and Damage token promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateStressManifest(
        string path,
        string repository,
        string canonicalMesh,
        string canonicalFace,
        string reviewMesh,
        string reviewFace,
        ObjInspection mesh,
        ImageInspection face,
        string meshHash,
        string faceHash)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
            throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = manifest["Assets"]?.AsArray() ?? throw new InvalidDataException("Core manifest does not contain Assets.");
        SetMeshAsset(FindAsset(assets, "stress-mesh"), "stress-mesh", "stress", canonicalMesh, reviewMesh, repository, mesh, meshHash);
        SetFaceAsset(FindAsset(assets, "stress-face"), "stress-face", "stress", canonicalFace, reviewFace, repository, face, faceHash);
        manifest["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        manifest["StressPromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["ReviewMeshPath"] = Relative(repository, reviewMesh),
            ["ReviewFacePath"] = Relative(repository, reviewFace),
            ["ArtworkProcessing"] = "user-adjusted-red-colour; toolkit-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(path, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static void WriteMissionManifest(
        string path,
        string repository,
        string canonicalMesh,
        string canonicalFace,
        string reviewMesh,
        string reviewFace,
        string geometryReference,
        ObjInspection mesh,
        ImageInspection face,
        string meshHash,
        string faceHash)
    {
        var manifest = new JsonObject
        {
            ["SchemaVersion"] = "1.0.0",
            ["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["Policy"] = "First Edition mission tokens are catalogued separately from core gameplay tokens. Runtime Lua is unchanged.",
            ["AssetCount"] = 2,
            ["TokenCount"] = 1,
            ["Assets"] = new JsonArray
            {
                CreateMeshAsset("damage-mesh", "damage", canonicalMesh, reviewMesh, repository, mesh, meshHash),
                CreateFaceAsset("damage-face", "damage", canonicalFace, reviewFace, repository, face, faceHash)
            },
            ["Tokens"] = new JsonArray
            {
                new JsonObject
                {
                    ["Id"] = "damage",
                    ["Name"] = "Damage Token",
                    ["Category"] = "mission-token",
                    ["FaceSemantics"] = new JsonArray { "1", "2" },
                    ["MeshAssetId"] = "damage-mesh",
                    ["FaceAssetId"] = "damage-face",
                    ["MeshPath"] = Relative(repository, canonicalMesh),
                    ["FacePath"] = Relative(repository, canonicalFace),
                    ["GeometryReferencePath"] = Relative(repository, geometryReference),
                    ["RuntimeStatus"] = "asset-validation-only",
                    ["LuaIncluded"] = false,
                    ["VisualValidation"] = "approved-in-tabletop-simulator"
                }
            }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static JsonObject CreateMeshAsset(string id, string tokenId, string canonical, string review, string repository, ObjInspection mesh, string hash)
    {
        var result = new JsonObject();
        SetMeshAsset(result, id, tokenId, canonical, review, repository, mesh, hash);
        return result;
    }

    private static JsonObject CreateFaceAsset(string id, string tokenId, string canonical, string review, string repository, ImageInspection face, string hash)
    {
        var result = new JsonObject();
        SetFaceAsset(result, id, tokenId, canonical, review, repository, face, hash);
        return result;
    }

    private static void SetMeshAsset(JsonObject asset, string id, string tokenId, string canonical, string review, string repository, ObjInspection mesh, string hash)
    {
        SetCommon(asset, id, tokenId, "mesh", canonical, review, repository, hash);
        asset["Width"] = null;
        asset["Height"] = null;
        asset["HasAlpha"] = null;
        asset["MeshBounds"] = new JsonObject
        {
            ["VertexCount"] = mesh.VertexCount,
            ["Width"] = mesh.Width,
            ["Height"] = mesh.Height,
            ["Depth"] = mesh.Depth
        };
        asset["SourceKind"] = "uv-remapped-validated-mesh";
    }

    private static void SetFaceAsset(JsonObject asset, string id, string tokenId, string canonical, string review, string repository, ImageInspection face, string hash)
    {
        SetCommon(asset, id, tokenId, "texture", canonical, review, repository, hash);
        asset["Width"] = face.Width;
        asset["Height"] = face.Height;
        asset["HasAlpha"] = face.HasAlpha;
        asset["MeshBounds"] = null;
        asset["SourceKind"] = "user-supplied-high-resolution-scan";
    }

    private static void SetCommon(JsonObject asset, string id, string tokenId, string role, string canonical, string review, string repository, string hash)
    {
        asset["Id"] = id;
        asset["TokenIds"] = new JsonArray { tokenId };
        asset["Role"] = role;
        asset["RepositoryPath"] = Relative(repository, canonical);
        asset["SizeBytes"] = new FileInfo(canonical).Length;
        asset["Sha256"] = hash;
        asset["OriginalSourceUrl"] = null;
        asset["Resolution"] = "approved-review-promotion";
        asset["ResolvedFrom"] = Relative(repository, review);
        asset["SourceKind"] = "user-supplied-high-resolution-scan";
    }

    private static void ValidateMesh(string name, ObjInspection original, ObjInspection review)
    {
        if (review.VertexCount != original.VertexCount || review.FaceCount != original.FaceCount)
            throw new InvalidDataException($"{name} mesh vertex or face count changed.");
        if (!original.Vertices.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(review.Vertices.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException($"{name} vertex positions differ from the validated reference mesh.");
        if (!NearlyEqual(original.Width, review.Width) || !NearlyEqual(original.Height, review.Height) || !NearlyEqual(original.Depth, review.Depth))
            throw new InvalidDataException($"{name} physical mesh bounds changed.");
        if (review.UvCount == 0) throw new InvalidDataException($"{name} review mesh has no UV coordinates.");
    }

    private static void ValidateSquareFace(string name, ImageInspection original, ImageInspection review)
    {
        if (review.Width < original.Width || review.Height < original.Height)
            throw new InvalidDataException($"{name} review texture is lower resolution than its canonical texture.");
        if (review.Width != review.Height)
            throw new InvalidDataException($"{name} review texture must be square; found {review.Width}x{review.Height}.");
    }

    private static ObjInspection InspectObj(string path)
    {
        var vertices = new List<string>();
        var coordinates = new List<(double X, double Y, double Z)>();
        var uvCount = 0;
        var faceCount = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("vt ", StringComparison.Ordinal)) { uvCount++; continue; }
            if (line.StartsWith("f ", StringComparison.Ordinal)) { faceCount++; continue; }
            if (!line.StartsWith("v ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException($"Unreadable OBJ vertex in {path}: {line}");
            coordinates.Add((x, y, z));
            vertices.Add($"{x:R}|{y:R}|{z:R}");
        }
        if (coordinates.Count == 0) throw new InvalidDataException($"OBJ contains no vertices: {path}");
        return new ObjInspection
        {
            VertexCount = coordinates.Count,
            UvCount = uvCount,
            FaceCount = faceCount,
            Vertices = vertices,
            Width = coordinates.Max(item => item.X) - coordinates.Min(item => item.X),
            Height = coordinates.Max(item => item.Y) - coordinates.Min(item => item.Y),
            Depth = coordinates.Max(item => item.Z) - coordinates.Min(item => item.Z)
        };
    }

    private static ImageInspection InspectImage(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInspection { Width = bitmap.Width, Height = bitmap.Height, HasAlpha = bitmap.AlphaType != SKAlphaType.Opaque };
    }

    private static JsonObject FindAsset(JsonArray assets, string id) => assets.OfType<JsonObject>().FirstOrDefault(asset =>
        string.Equals(asset["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidDataException($"Manifest asset not found: {id}");
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-stress-damage-tokens <first-edition-repo-folder> [--no-knowledge-base]");

    private sealed class ObjInspection
    {
        public int VertexCount { get; init; }
        public int UvCount { get; init; }
        public int FaceCount { get; init; }
        public List<string> Vertices { get; init; } = [];
        public double Width { get; init; }
        public double Height { get; init; }
        public double Depth { get; init; }
    }

    private sealed class ImageInspection
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public bool HasAlpha { get; init; }
    }
}
