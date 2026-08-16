using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R13 promotes the approved high-resolution Critical Hit scan and
/// introduces the approved clipped-square Ordnance token. Runtime Lua and
/// gameplay state are deliberately unchanged.
/// </summary>
public static class PromoteFirstEditionCriticalOrdnanceTokensCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R13 Critical Hit and Ordnance Token Promotion");
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

            var manifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/core-gameplay-tokens.json");
            var originalCriticalMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/critical-hit.obj");
            var reviewCriticalMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/critical_test_v2.obj");
            var reviewCriticalFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/critical_test.png");
            var canonicalCriticalMesh = originalCriticalMesh;
            var canonicalCriticalFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/critical-hit.png");

            var reviewOrdnanceMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/ordnance_test.obj");
            var reviewOrdnanceFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/ordnance_test.png");
            var canonicalOrdnanceMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/ordnance.obj");
            var canonicalOrdnanceFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/ordnance.png");

            foreach (var path in new[]
            {
                manifestPath, originalCriticalMesh, reviewCriticalMesh, reviewCriticalFace,
                canonicalCriticalFace, reviewOrdnanceMesh, reviewOrdnanceFace
            })
                RequireFile(path, "Required promotion input");

            var oldCritical = InspectObj(originalCriticalMesh);
            var newCritical = InspectObj(reviewCriticalMesh);
            ValidateSameGeometry("Critical Hit", oldCritical, newCritical);
            var criticalFace = InspectImage(reviewCriticalFace);
            ValidateTexture("Critical Hit", criticalFace, 420, 420);

            var ordnance = InspectObj(reviewOrdnanceMesh);
            ValidateOrdnance(ordnance);
            var ordnanceFace = InspectImage(reviewOrdnanceFace);
            ValidateTexture("Ordnance", ordnanceFace, 420, 420);

            File.Copy(reviewCriticalMesh, canonicalCriticalMesh, true);
            File.Copy(reviewCriticalFace, canonicalCriticalFace, true);
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalOrdnanceMesh)!);
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalOrdnanceFace)!);
            File.Copy(reviewOrdnanceMesh, canonicalOrdnanceMesh, true);
            File.Copy(reviewOrdnanceFace, canonicalOrdnanceFace, true);

            var criticalMeshHash = HashFile(canonicalCriticalMesh);
            var criticalFaceHash = HashFile(canonicalCriticalFace);
            var ordnanceMeshHash = HashFile(canonicalOrdnanceMesh);
            var ordnanceFaceHash = HashFile(canonicalOrdnanceFace);

            UpdateManifest(
                manifestPath,
                repository,
                reviewCriticalMesh,
                reviewCriticalFace,
                canonicalCriticalMesh,
                canonicalCriticalFace,
                newCritical,
                criticalFace,
                criticalMeshHash,
                criticalFaceHash,
                reviewOrdnanceMesh,
                reviewOrdnanceFace,
                canonicalOrdnanceMesh,
                canonicalOrdnanceFace,
                ordnance,
                ordnanceFace,
                ordnanceMeshHash,
                ordnanceFaceHash);

            var refreshKnowledgeBase = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refreshKnowledgeBase)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                 {repository}");
            Console.WriteLine($"Critical Hit mesh:          {canonicalCriticalMesh}");
            Console.WriteLine($"Critical Hit face:          {canonicalCriticalFace}");
            Console.WriteLine($"Critical Hit texture:       {criticalFace.Width} x {criticalFace.Height}");
            Console.WriteLine($"Critical Hit mesh SHA-256:  {criticalMeshHash}");
            Console.WriteLine($"Critical Hit face SHA-256:  {criticalFaceHash}");
            Console.WriteLine($"Ordnance mesh:              {canonicalOrdnanceMesh}");
            Console.WriteLine($"Ordnance face:              {canonicalOrdnanceFace}");
            Console.WriteLine($"Ordnance texture:           {ordnanceFace.Width} x {ordnanceFace.Height}");
            Console.WriteLine($"Ordnance mesh SHA-256:      {ordnanceMeshHash}");
            Console.WriteLine($"Ordnance face SHA-256:      {ordnanceFaceHash}");
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine($"Knowledge base refresh:     {(refreshKnowledgeBase ? "Yes" : "No")}");
            Console.WriteLine("Images modified:            0");
            Console.WriteLine("Lua scripts modified:       0");
            Console.WriteLine();
            Console.WriteLine("Critical Hit and Ordnance tokens promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Critical Hit and Ordnance promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateManifest(
        string manifestPath,
        string repository,
        string reviewCriticalMesh,
        string reviewCriticalFace,
        string canonicalCriticalMesh,
        string canonicalCriticalFace,
        ObjInspection criticalMesh,
        ImageInspection criticalFace,
        string criticalMeshHash,
        string criticalFaceHash,
        string reviewOrdnanceMesh,
        string reviewOrdnanceFace,
        string canonicalOrdnanceMesh,
        string canonicalOrdnanceFace,
        ObjInspection ordnanceMesh,
        ImageInspection ordnanceFace,
        string ordnanceMeshHash,
        string ordnanceFaceHash)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ??
            throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = manifest["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = manifest["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");

        SetMeshAsset(FindAsset(assets, "critical-hit-mesh"), "critical-hit-mesh", "critical-hit",
            repository, canonicalCriticalMesh, reviewCriticalMesh, criticalMesh, criticalMeshHash);
        SetFaceAsset(FindAsset(assets, "critical-hit-face"), "critical-hit-face", "critical-hit",
            repository, canonicalCriticalFace, reviewCriticalFace, criticalFace, criticalFaceHash);

        SetMeshAsset(FindOrAddAsset(assets, "ordnance-mesh"), "ordnance-mesh", "ordnance",
            repository, canonicalOrdnanceMesh, reviewOrdnanceMesh, ordnanceMesh, ordnanceMeshHash);
        SetFaceAsset(FindOrAddAsset(assets, "ordnance-face"), "ordnance-face", "ordnance",
            repository, canonicalOrdnanceFace, reviewOrdnanceFace, ordnanceFace, ordnanceFaceHash);

        var criticalToken = FindToken(tokens, "critical-hit");
        SetToken(criticalToken, "critical-hit", "Critical Hit", "critical-hit-mesh", "critical-hit-face",
            Relative(repository, canonicalCriticalMesh), Relative(repository, canonicalCriticalFace));

        var ordnanceToken = tokens.OfType<JsonObject>().FirstOrDefault(token =>
            string.Equals(token["Id"]?.GetValue<string>(), "ordnance", StringComparison.OrdinalIgnoreCase));
        if (ordnanceToken is null)
        {
            ordnanceToken = new JsonObject();
            tokens.Add(ordnanceToken);
        }
        SetToken(ordnanceToken, "ordnance", "Ordnance", "ordnance-mesh", "ordnance-face",
            Relative(repository, canonicalOrdnanceMesh), Relative(repository, canonicalOrdnanceFace));

        manifest["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        manifest["AssetCount"] = assets.Count;
        manifest["TokenCount"] = tokens.Count;
        manifest["Policy"] = "Original First Edition assets promoted byte-for-byte. Approved high-resolution physical scans use validated UV-remapped meshes. Runtime Lua is unchanged.";
        manifest["CriticalOrdnancePromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["CriticalReviewMeshPath"] = Relative(repository, reviewCriticalMesh),
            ["CriticalReviewFacePath"] = Relative(repository, reviewCriticalFace),
            ["OrdnanceReviewMeshPath"] = Relative(repository, reviewOrdnanceMesh),
            ["OrdnanceReviewFacePath"] = Relative(repository, reviewOrdnanceFace),
            ["OrdnanceGeometry"] = "approved-phase16e-r7-clipped-square",
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static void SetMeshAsset(JsonObject asset, string id, string tokenId, string repository,
        string canonical, string review, ObjInspection mesh, string hash)
    {
        asset["Id"] = id;
        asset["TokenIds"] = new JsonArray(tokenId);
        asset["Role"] = "mesh";
        SetCommon(asset, repository, canonical, review, hash);
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
    }

    private static void SetFaceAsset(JsonObject asset, string id, string tokenId, string repository,
        string canonical, string review, ImageInspection face, string hash)
    {
        asset["Id"] = id;
        asset["TokenIds"] = new JsonArray(tokenId);
        asset["Role"] = "texture";
        SetCommon(asset, repository, canonical, review, hash);
        asset["Width"] = face.Width;
        asset["Height"] = face.Height;
        asset["HasAlpha"] = face.HasAlpha;
        asset["MeshBounds"] = null;
    }

    private static void SetCommon(JsonObject asset, string repository, string canonical, string review, string hash)
    {
        asset["RepositoryPath"] = Relative(repository, canonical);
        asset["SizeBytes"] = new FileInfo(canonical).Length;
        asset["Sha256"] = hash;
        asset["OriginalSourceUrl"] = null;
        asset["Resolution"] = "approved-review-promotion";
        asset["ResolvedFrom"] = Relative(repository, review);
        asset["SourceKind"] = "user-supplied-high-resolution-scan";
    }

    private static void SetToken(JsonObject token, string id, string name, string meshAssetId, string faceAssetId,
        string meshPath, string facePath)
    {
        token["Id"] = id;
        token["Name"] = name;
        token["MeshAssetId"] = meshAssetId;
        token["FaceAssetId"] = faceAssetId;
        token["MeshPath"] = meshPath;
        token["FacePath"] = facePath;
        token["RuntimeStatus"] = "asset-validation-only";
        token["LuaIncluded"] = false;
    }

    private static void ValidateSameGeometry(string name, ObjInspection original, ObjInspection review)
    {
        if (review.VertexCount != original.VertexCount || review.FaceCount != original.FaceCount)
            throw new InvalidDataException($"{name} mesh vertex or face count changed.");
        if (!original.Vertices.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(review.Vertices.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException($"{name} vertex positions differ from the canonical mesh.");
        if (!NearlyEqual(original.Width, review.Width) || !NearlyEqual(original.Height, review.Height) || !NearlyEqual(original.Depth, review.Depth))
            throw new InvalidDataException($"{name} physical mesh bounds changed.");
        if (review.UvCount == 0) throw new InvalidDataException($"{name} review mesh has no UV coordinates.");
    }

    private static void ValidateOrdnance(ObjInspection mesh)
    {
        if (mesh.VertexCount != 48 || mesh.UvCount != 48 || mesh.FaceCount != 20)
            throw new InvalidDataException($"Ordnance mesh topology differs from the approved R7 prototype: {mesh.VertexCount} vertices, {mesh.UvCount} UVs, {mesh.FaceCount} faces.");
        if (!NearlyEqual(mesh.Width, 2.0) || !NearlyEqual(mesh.Height, 0.18) || !NearlyEqual(mesh.Depth, 2.0))
            throw new InvalidDataException($"Ordnance mesh bounds differ from the approved R7 prototype: {mesh.Width} x {mesh.Height} x {mesh.Depth}.");
    }

    private static void ValidateTexture(string name, ImageInspection face, int width, int height)
    {
        if (face.Width != width || face.Height != height)
            throw new InvalidDataException($"{name} texture must be {width}x{height}; found {face.Width}x{face.Height}.");
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
        return new ObjInspection(
            coordinates.Count,
            uvCount,
            faceCount,
            vertices,
            coordinates.Max(item => item.X) - coordinates.Min(item => item.X),
            coordinates.Max(item => item.Y) - coordinates.Min(item => item.Y),
            coordinates.Max(item => item.Z) - coordinates.Min(item => item.Z));
    }

    private static ImageInspection InspectImage(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInspection(bitmap.Width, bitmap.Height, bitmap.AlphaType != SKAlphaType.Opaque);
    }

    private static JsonObject FindAsset(JsonArray assets, string id) => assets.OfType<JsonObject>().FirstOrDefault(asset =>
        string.Equals(asset["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidDataException($"Manifest asset not found: {id}");
    private static JsonObject FindOrAddAsset(JsonArray assets, string id)
    {
        var asset = assets.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));
        if (asset is not null) return asset;
        asset = new JsonObject();
        assets.Add(asset);
        return asset;
    }
    private static JsonObject FindToken(JsonArray tokens, string id) => tokens.OfType<JsonObject>().FirstOrDefault(token =>
        string.Equals(token["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidDataException($"Manifest token not found: {id}");
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-critical-ordnance-tokens <first-edition-repo-folder> [--no-knowledge-base]");

    private sealed record ObjInspection(int VertexCount, int UvCount, int FaceCount, List<string> Vertices, double Width, double Height, double Depth);
    private sealed record ImageInspection(int Width, int Height, bool HasAlpha);
}
