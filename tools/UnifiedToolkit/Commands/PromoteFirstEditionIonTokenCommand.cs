using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

public static class PromoteFirstEditionIonTokenCommand
{
    private const string ApprovedMeshHash = "79c44b59c6f35b08990f3a321113937f240c74dd8865c9b6cf972e897cc294ea";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R15 High-Resolution Ion Token Promotion");
        Console.WriteLine("=================================================================");
        Console.WriteLine();
        if (args.Length < 1) { ShowUsage(); return 1; }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            RequireDirectory(repository, "Repository");
            var canonicalMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/ion.obj");
            var canonicalFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/ion.png");
            var reviewMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/ion_test_v2.obj");
            var reviewFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/ion_test.png");
            var manifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/core-gameplay-tokens.json");
            foreach (var path in new[] { canonicalMesh, canonicalFace, reviewMesh, reviewFace, manifestPath })
                RequireFile(path, "Required promotion input");

            var original = InspectObj(canonicalMesh);
            var approved = InspectObj(reviewMesh);
            ValidateMesh(original, approved, HashFile(reviewMesh));
            var face = InspectImage(reviewFace);
            if (face.Width != 420 || face.Height != 420)
                throw new InvalidDataException($"Ion review texture must be 420x420; found {face.Width}x{face.Height}.");

            File.Copy(reviewMesh, canonicalMesh, true);
            File.Copy(reviewFace, canonicalFace, true);
            var meshHash = HashFile(canonicalMesh);
            var faceHash = HashFile(canonicalFace);
            UpdateManifest(manifestPath, repository, reviewMesh, reviewFace, canonicalMesh, canonicalFace,
                approved, face, meshHash, faceHash);

            var refresh = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refresh)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:             {repository}");
            Console.WriteLine($"Canonical Ion mesh:     {canonicalMesh}");
            Console.WriteLine($"Canonical Ion face:     {canonicalFace}");
            Console.WriteLine($"Texture:                {face.Width} x {face.Height}");
            Console.WriteLine("Edge mapping:           black scan background");
            Console.WriteLine($"Mesh SHA-256:           {meshHash}");
            Console.WriteLine($"Face SHA-256:           {faceHash}");
            Console.WriteLine($"Manifest:               {manifestPath}");
            Console.WriteLine($"Knowledge base refresh: {(refresh ? "Yes" : "No")}");
            Console.WriteLine("Images modified:        0");
            Console.WriteLine("Lua scripts modified:   0");
            Console.WriteLine();
            Console.WriteLine("High-resolution Ion token promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ion promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateManifest(string path, string repository, string reviewMesh, string reviewFace,
        string canonicalMesh, string canonicalFace, ObjInfo mesh, ImageInfo face, string meshHash, string faceHash)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = root["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = root["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");
        var meshAsset = Find(assets, "ion-mesh");
        var faceAsset = Find(assets, "ion-face");
        var token = Find(tokens, "ion");

        SetCommon(meshAsset, repository, canonicalMesh, reviewMesh, meshHash);
        meshAsset["TokenIds"] = new JsonArray("ion");
        meshAsset["Role"] = "mesh";
        meshAsset["Width"] = null; meshAsset["Height"] = null; meshAsset["HasAlpha"] = null;
        meshAsset["MeshBounds"] = new JsonObject
        {
            ["VertexCount"] = mesh.VertexCount, ["Width"] = mesh.Width,
            ["Height"] = mesh.Height, ["Depth"] = mesh.Depth
        };
        SetCommon(faceAsset, repository, canonicalFace, reviewFace, faceHash);
        faceAsset["TokenIds"] = new JsonArray("ion");
        faceAsset["Role"] = "texture";
        faceAsset["Width"] = face.Width; faceAsset["Height"] = face.Height;
        faceAsset["HasAlpha"] = face.HasAlpha; faceAsset["MeshBounds"] = null;
        token["MeshPath"] = Relative(repository, canonicalMesh);
        token["FacePath"] = Relative(repository, canonicalFace);

        root["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        root["AssetCount"] = assets.Count;
        root["TokenCount"] = tokens.Count;
        root["Policy"] = "Original First Edition assets promoted byte-for-byte. Approved scans use validated UV-remapped meshes. Runtime Lua is unchanged.";
        root["IonPromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["ReviewMeshPath"] = Relative(repository, reviewMesh),
            ["ReviewFacePath"] = Relative(repository, reviewFace),
            ["GeometryPolicy"] = "canonical-round-ion-geometry-retained",
            ["EdgeUvPolicy"] = "mapped-to-black-scan-background",
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(path, root.ToJsonString(JsonOptions), new UTF8Encoding(false));
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

    private static void ValidateMesh(ObjInfo original, ObjInfo review, string hash)
    {
        if (!hash.Equals(ApprovedMeshHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ion review mesh does not match approved R2.");
        if (review.VertexCount != original.VertexCount || review.FaceCount != original.FaceCount) throw new InvalidDataException("Ion topology changed.");
        if (!original.Vertices.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(review.Vertices.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Ion vertex positions differ from the canonical mesh.");
        if (!Near(original.Width, review.Width) || !Near(original.Height, review.Height) || !Near(original.Depth, review.Depth)) throw new InvalidDataException("Ion bounds changed.");
        if (review.UvCount == 0) throw new InvalidDataException("Ion review mesh has no UV coordinates.");
    }

    private static ObjInfo InspectObj(string path)
    {
        var vertices = new List<string>(); var points = new List<(double X, double Y, double Z)>(); var uv = 0; var faces = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("vt ")) { uv++; continue; }
            if (line.StartsWith("f ")) { faces++; continue; }
            if (!line.StartsWith("v ")) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) throw new InvalidDataException($"Unreadable OBJ vertex: {line}");
            points.Add((x, y, z)); vertices.Add($"{x:R}|{y:R}|{z:R}");
        }
        return new ObjInfo(points.Count, uv, faces, vertices,
            points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y), points.Max(p => p.Z) - points.Min(p => p.Z));
    }

    private static ImageInfo InspectImage(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInfo(bitmap.Width, bitmap.Height, bitmap.AlphaType != SKAlphaType.Opaque);
    }

    private static JsonObject Find(JsonArray items, string id) => items.OfType<JsonObject>().FirstOrDefault(item =>
        string.Equals(item["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException($"Manifest item not found: {id}");
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool Near(double left, double right) => Math.Abs(left - right) <= 0.000001;
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-ion-token <first-edition-repo-folder> [--no-knowledge-base]");
    private sealed record ObjInfo(int VertexCount, int UvCount, int FaceCount, List<string> Vertices, double Width, double Height, double Depth);
    private sealed record ImageInfo(int Width, int Height, bool HasAlpha);
}
