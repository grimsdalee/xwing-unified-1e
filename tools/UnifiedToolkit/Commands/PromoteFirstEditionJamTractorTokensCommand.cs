using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R14 promotes the approved regular-octagonal Jam token and the
/// approved clipped-diamond Tractor Beam token. Runtime Lua is unchanged.
/// </summary>
public static class PromoteFirstEditionJamTractorTokensCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly PromotionInput[] Inputs =
    [
        new(
            "Jam",
            "jam",
            "jam-mesh",
            "jam-face",
            "assets/source/unified1e/gameplay-tokens/review/jam_test.obj",
            "assets/source/unified1e/gameplay-tokens/review/jam_test.png",
            "assets/source/unified1e/gameplay-tokens/meshes/jam.obj",
            "assets/source/unified1e/gameplay-tokens/faces/jam.png",
            "1b2742899d72c499c1656dafad4bbdb7888ee15fd80df01f4265fff067ea35bd",
            2.0,
            0.18,
            2.0,
            "regular-octagon"),
        new(
            "Tractor Beam",
            "tractor-beam",
            "tractor-beam-mesh",
            "tractor-beam-face",
            "assets/source/unified1e/gameplay-tokens/review/tractor-beam_test.obj",
            "assets/source/unified1e/gameplay-tokens/review/tractor-beam_test.png",
            "assets/source/unified1e/gameplay-tokens/meshes/tractor-beam.obj",
            "assets/source/unified1e/gameplay-tokens/faces/tractor-beam.png",
            "13150baf72c5387f3b793405b4ceea157db1791283ec4546a8dc06ede93d12c1",
            2.0,
            0.18,
            2.262068,
            "vertically-proportioned-clipped-diamond")
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R14 Jam and Tractor Beam Token Promotion");
        Console.WriteLine("==================================================================");
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
            RequireFile(manifestPath, "Core gameplay-token manifest");

            var results = new List<PromotionResult>();
            foreach (var input in Inputs)
            {
                var reviewMesh = Resolve(repository, input.ReviewMeshPath);
                var reviewFace = Resolve(repository, input.ReviewFacePath);
                var canonicalMesh = Resolve(repository, input.CanonicalMeshPath);
                var canonicalFace = Resolve(repository, input.CanonicalFacePath);
                RequireFile(reviewMesh, $"{input.Name} review mesh");
                RequireFile(reviewFace, $"{input.Name} review face");

                var mesh = InspectObj(reviewMesh);
                ValidateMesh(input, mesh, HashFile(reviewMesh));
                var face = InspectImage(reviewFace);
                if (face.Width != 420 || face.Height != 420)
                    throw new InvalidDataException($"{input.Name} texture must be 420x420; found {face.Width}x{face.Height}.");

                Directory.CreateDirectory(Path.GetDirectoryName(canonicalMesh)!);
                Directory.CreateDirectory(Path.GetDirectoryName(canonicalFace)!);
                File.Copy(reviewMesh, canonicalMesh, true);
                File.Copy(reviewFace, canonicalFace, true);
                results.Add(new PromotionResult(input, mesh, face, HashFile(canonicalMesh), HashFile(canonicalFace)));
            }

            UpdateManifest(manifestPath, repository, results);

            var refreshKnowledgeBase = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refreshKnowledgeBase)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                  {repository}");
            foreach (var result in results)
            {
                Console.WriteLine($"{result.Input.Name} mesh: {Resolve(repository, result.Input.CanonicalMeshPath)}");
                Console.WriteLine($"{result.Input.Name} face: {Resolve(repository, result.Input.CanonicalFacePath)}");
                Console.WriteLine($"{result.Input.Name} texture: {result.Face.Width} x {result.Face.Height}");
                Console.WriteLine($"{result.Input.Name} geometry: {result.Input.Geometry}");
                Console.WriteLine($"{result.Input.Name} mesh SHA-256: {result.MeshHash}");
                Console.WriteLine($"{result.Input.Name} face SHA-256: {result.FaceHash}");
            }
            Console.WriteLine($"Manifest:                    {manifestPath}");
            Console.WriteLine($"Knowledge base refresh:      {(refreshKnowledgeBase ? "Yes" : "No")}");
            Console.WriteLine("Images modified:             0");
            Console.WriteLine("Lua scripts modified:        0");
            Console.WriteLine();
            Console.WriteLine("Jam and Tractor Beam tokens promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Jam and Tractor Beam promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateManifest(string manifestPath, string repository, IReadOnlyList<PromotionResult> results)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ??
            throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = manifest["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = manifest["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");

        foreach (var result in results)
        {
            SetMeshAsset(FindOrAddAsset(assets, result.Input.MeshAssetId), result, repository);
            SetFaceAsset(FindOrAddAsset(assets, result.Input.FaceAssetId), result, repository);
            var token = tokens.OfType<JsonObject>().FirstOrDefault(item =>
                string.Equals(item["Id"]?.GetValue<string>(), result.Input.TokenId, StringComparison.OrdinalIgnoreCase));
            if (token is null)
            {
                token = new JsonObject();
                tokens.Add(token);
            }
            token["Id"] = result.Input.TokenId;
            token["Name"] = result.Input.Name;
            token["MeshAssetId"] = result.Input.MeshAssetId;
            token["FaceAssetId"] = result.Input.FaceAssetId;
            token["MeshPath"] = result.Input.CanonicalMeshPath;
            token["FacePath"] = result.Input.CanonicalFacePath;
            token["RuntimeStatus"] = "asset-validation-only";
            token["LuaIncluded"] = false;
        }

        manifest["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        manifest["AssetCount"] = assets.Count;
        manifest["TokenCount"] = tokens.Count;
        manifest["Policy"] = "Original First Edition assets promoted byte-for-byte. Approved high-resolution physical scans use validated custom token meshes. Runtime Lua is unchanged.";
        manifest["JamTractorPromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["JamReviewMeshPath"] = Inputs[0].ReviewMeshPath,
            ["JamReviewFacePath"] = Inputs[0].ReviewFacePath,
            ["JamGeometry"] = Inputs[0].Geometry,
            ["TractorBeamReviewMeshPath"] = Inputs[1].ReviewMeshPath,
            ["TractorBeamReviewFacePath"] = Inputs[1].ReviewFacePath,
            ["TractorBeamGeometry"] = Inputs[1].Geometry,
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static void SetMeshAsset(JsonObject asset, PromotionResult result, string repository)
    {
        asset["Id"] = result.Input.MeshAssetId;
        asset["TokenIds"] = new JsonArray(result.Input.TokenId);
        asset["Role"] = "mesh";
        SetCommon(asset, result.Input.CanonicalMeshPath, result.Input.ReviewMeshPath,
            Resolve(repository, result.Input.CanonicalMeshPath), result.MeshHash);
        asset["Width"] = null;
        asset["Height"] = null;
        asset["HasAlpha"] = null;
        asset["MeshBounds"] = new JsonObject
        {
            ["VertexCount"] = result.Mesh.VertexCount,
            ["Width"] = result.Mesh.Width,
            ["Height"] = result.Mesh.Height,
            ["Depth"] = result.Mesh.Depth
        };
    }

    private static void SetFaceAsset(JsonObject asset, PromotionResult result, string repository)
    {
        asset["Id"] = result.Input.FaceAssetId;
        asset["TokenIds"] = new JsonArray(result.Input.TokenId);
        asset["Role"] = "texture";
        SetCommon(asset, result.Input.CanonicalFacePath, result.Input.ReviewFacePath,
            Resolve(repository, result.Input.CanonicalFacePath), result.FaceHash);
        asset["Width"] = result.Face.Width;
        asset["Height"] = result.Face.Height;
        asset["HasAlpha"] = result.Face.HasAlpha;
        asset["MeshBounds"] = null;
    }

    private static void SetCommon(JsonObject asset, string canonicalPath, string reviewPath, string canonicalFile, string hash)
    {
        asset["RepositoryPath"] = canonicalPath;
        asset["SizeBytes"] = new FileInfo(canonicalFile).Length;
        asset["Sha256"] = hash;
        asset["OriginalSourceUrl"] = null;
        asset["Resolution"] = "approved-review-promotion";
        asset["ResolvedFrom"] = reviewPath;
        asset["SourceKind"] = "user-supplied-high-resolution-scan";
    }

    private static void ValidateMesh(PromotionInput input, ObjInspection mesh, string hash)
    {
        if (!hash.Equals(input.ApprovedMeshSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{input.Name} mesh does not match the visually approved review mesh.");
        if (mesh.VertexCount != 16 || mesh.UvCount != 48 || mesh.FaceCount != 20)
            throw new InvalidDataException($"{input.Name} topology is invalid: {mesh.VertexCount} vertices, {mesh.UvCount} UVs, {mesh.FaceCount} faces.");
        if (!NearlyEqual(mesh.Width, input.Width) || !NearlyEqual(mesh.Height, input.Height) || !NearlyEqual(mesh.Depth, input.Depth))
            throw new InvalidDataException($"{input.Name} bounds are invalid: {mesh.Width} x {mesh.Height} x {mesh.Depth}.");
    }

    private static ObjInspection InspectObj(string path)
    {
        var points = new List<(double X, double Y, double Z)>();
        var uvCount = 0;
        var faceCount = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("vt ", StringComparison.Ordinal)) { uvCount++; continue; }
            if (line.StartsWith("f ", StringComparison.Ordinal)) { faceCount++; continue; }
            if (!line.StartsWith("v ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException($"Unreadable OBJ vertex in {path}: {line}");
            points.Add((x, y, z));
        }
        if (points.Count == 0) throw new InvalidDataException($"OBJ contains no vertices: {path}");
        return new ObjInspection(
            points.Count,
            uvCount,
            faceCount,
            points.Max(item => item.X) - points.Min(item => item.X),
            points.Max(item => item.Y) - points.Min(item => item.Y),
            points.Max(item => item.Z) - points.Min(item => item.Z));
    }

    private static ImageInspection InspectImage(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInspection(bitmap.Width, bitmap.Height, bitmap.AlphaType != SKAlphaType.Opaque);
    }

    private static JsonObject FindOrAddAsset(JsonArray assets, string id)
    {
        var asset = assets.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));
        if (asset is not null) return asset;
        asset = new JsonObject();
        assets.Add(asset);
        return asset;
    }
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-jam-tractor-tokens <first-edition-repo-folder> [--no-knowledge-base]");

    private sealed record PromotionInput(string Name, string TokenId, string MeshAssetId, string FaceAssetId,
        string ReviewMeshPath, string ReviewFacePath, string CanonicalMeshPath, string CanonicalFacePath,
        string ApprovedMeshSha256, double Width, double Height, double Depth, string Geometry);
    private sealed record PromotionResult(PromotionInput Input, ObjInspection Mesh, ImageInspection Face, string MeshHash, string FaceHash);
    private sealed record ObjInspection(int VertexCount, int UvCount, int FaceCount, double Width, double Height, double Depth);
    private sealed record ImageInspection(int Width, int Height, bool HasAlpha);
}
