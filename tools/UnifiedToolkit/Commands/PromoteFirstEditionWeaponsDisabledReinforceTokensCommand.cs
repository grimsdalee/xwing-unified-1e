using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R12 promotes the approved high-resolution Weapons Disabled,
/// Epic Reinforce and small-ship Reinforce token scans with their validated
/// UV-remapped round meshes. Existing runtime Lua is deliberately unchanged.
/// </summary>
public static class PromoteFirstEditionWeaponsDisabledReinforceTokensCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R12 Weapons Disabled and Reinforce Token Promotion");
        Console.WriteLine("============================================================================");
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

            var tokens = new[]
            {
                new PromotionInput(
                    "Weapons Disabled",
                    "weapons-disabled",
                    "assets/source/unified1e/gameplay-tokens/meshes/round-focus-weapons-disabled.obj",
                    "assets/source/unified1e/gameplay-tokens/review/weapon-disabled_test.obj",
                    "assets/source/unified1e/gameplay-tokens/review/weapon-disabled_test.png",
                    "assets/source/unified1e/gameplay-tokens/meshes/weapons-disabled.obj",
                    "assets/source/unified1e/gameplay-tokens/faces/weapons-disabled.png",
                    420,
                    420),
                new PromotionInput(
                    "Epic Reinforce",
                    "reinforce-epic",
                    "assets/source/unified1e/gameplay-tokens/meshes/reinforce.obj",
                    "assets/source/unified1e/gameplay-tokens/review/reinforce-epic_test.obj",
                    "assets/source/unified1e/gameplay-tokens/review/reinforce-epic_test.png",
                    "assets/source/unified1e/gameplay-tokens/meshes/reinforce-epic.obj",
                    "assets/source/unified1e/gameplay-tokens/faces/reinforce-epic.png",
                    420,
                    420),
                new PromotionInput(
                    "Small-ship Reinforce",
                    "reinforce",
                    "assets/source/unified1e/gameplay-tokens/meshes/reinforce.obj",
                    "assets/source/unified1e/gameplay-tokens/review/reinforce-small_test.obj",
                    "assets/source/unified1e/gameplay-tokens/review/reinforce-small_test.png",
                    "assets/source/unified1e/gameplay-tokens/meshes/reinforce-small.obj",
                    "assets/source/unified1e/gameplay-tokens/faces/reinforce-small.png",
                    840,
                    420)
            };

            RequireFile(manifestPath, "Core gameplay-token manifest");

            var promoted = new List<PromotionResult>();
            foreach (var input in tokens)
            {
                var referenceMesh = Resolve(repository, input.ReferenceMeshPath);
                var reviewMesh = Resolve(repository, input.ReviewMeshPath);
                var reviewFace = Resolve(repository, input.ReviewFacePath);
                var canonicalMesh = Resolve(repository, input.CanonicalMeshPath);
                var canonicalFace = Resolve(repository, input.CanonicalFacePath);
                RequireFile(referenceMesh, $"{input.Name} reference mesh");
                RequireFile(reviewMesh, $"{input.Name} review mesh");
                RequireFile(reviewFace, $"{input.Name} review face");

                var referenceInspection = InspectObj(referenceMesh);
                var mesh = InspectObj(reviewMesh);
                ValidateMesh(input.Name, referenceInspection, mesh);
                var face = InspectImage(reviewFace);
                ValidateFace(input, face);

                Directory.CreateDirectory(Path.GetDirectoryName(canonicalMesh)!);
                Directory.CreateDirectory(Path.GetDirectoryName(canonicalFace)!);
                File.Copy(reviewMesh, canonicalMesh, true);
                File.Copy(reviewFace, canonicalFace, true);

                promoted.Add(new PromotionResult(
                    input,
                    mesh,
                    face,
                    HashFile(canonicalMesh),
                    HashFile(canonicalFace)));
            }

            UpdateManifest(manifestPath, repository, promoted);

            var refreshKnowledgeBase = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refreshKnowledgeBase)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                       {repository}");
            foreach (var result in promoted)
            {
                Console.WriteLine($"{result.Input.Name} mesh: {Resolve(repository, result.Input.CanonicalMeshPath)}");
                Console.WriteLine($"{result.Input.Name} face: {Resolve(repository, result.Input.CanonicalFacePath)}");
                Console.WriteLine($"{result.Input.Name} texture: {result.Face.Width} x {result.Face.Height}");
                Console.WriteLine($"{result.Input.Name} mesh SHA-256: {result.MeshHash}");
                Console.WriteLine($"{result.Input.Name} face SHA-256: {result.FaceHash}");
            }
            Console.WriteLine($"Manifest:                         {manifestPath}");
            Console.WriteLine($"Knowledge base refresh:           {(refreshKnowledgeBase ? "Yes" : "No")}");
            Console.WriteLine("Images modified:                  0");
            Console.WriteLine("Lua scripts modified:             0");
            Console.WriteLine();
            Console.WriteLine("Weapons Disabled and Reinforce tokens promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Weapons Disabled and Reinforce promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateManifest(string manifestPath, string repository, IReadOnlyList<PromotionResult> promoted)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ??
            throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = manifest["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = manifest["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");

        var weapons = promoted.Single(item => item.Input.TokenId == "weapons-disabled");
        var small = promoted.Single(item => item.Input.TokenId == "reinforce");
        var epic = promoted.Single(item => item.Input.TokenId == "reinforce-epic");

        SetMeshAsset(FindAsset(assets, "round-focus-weapons-disabled-mesh"), "round-focus-weapons-disabled-mesh", weapons, repository);
        SetFaceAsset(FindAsset(assets, "weapons-disabled-face"), "weapons-disabled-face", weapons, repository);
        SetMeshAsset(FindAsset(assets, "reinforce-mesh"), "reinforce-mesh", small, repository);
        SetFaceAsset(FindAsset(assets, "reinforce-face"), "reinforce-face", small, repository);

        var epicMeshAsset = FindOrAddAsset(assets, "reinforce-epic-mesh");
        var epicFaceAsset = FindOrAddAsset(assets, "reinforce-epic-face");
        SetMeshAsset(epicMeshAsset, "reinforce-epic-mesh", epic, repository);
        SetFaceAsset(epicFaceAsset, "reinforce-epic-face", epic, repository);

        SetToken(FindToken(tokens, "weapons-disabled"), "weapons-disabled", "Weapon Disabled",
            "round-focus-weapons-disabled-mesh", "weapons-disabled-face", weapons.Input);
        SetToken(FindToken(tokens, "reinforce"), "reinforce", "Reinforce (small ship; fore/aft)",
            "reinforce-mesh", "reinforce-face", small.Input);

        var epicToken = tokens.OfType<JsonObject>().FirstOrDefault(token =>
            string.Equals(token["Id"]?.GetValue<string>(), "reinforce-epic", StringComparison.OrdinalIgnoreCase));
        if (epicToken is null)
        {
            epicToken = new JsonObject();
            tokens.Add(epicToken);
        }
        SetToken(epicToken, "reinforce-epic", "Reinforce (Epic; identical sides)",
            "reinforce-epic-mesh", "reinforce-epic-face", epic.Input);

        manifest["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        manifest["AssetCount"] = assets.Count;
        manifest["TokenCount"] = tokens.Count;
        manifest["Policy"] = "Original First Edition assets promoted byte-for-byte. Approved high-resolution physical scans use validated UV-remapped meshes. Small-ship and Epic Reinforce are distinct token definitions. Runtime Lua is unchanged.";
        manifest["WeaponsDisabledReinforcePromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["WeaponsDisabledReviewMeshPath"] = weapons.Input.ReviewMeshPath,
            ["WeaponsDisabledReviewFacePath"] = weapons.Input.ReviewFacePath,
            ["EpicReinforceReviewMeshPath"] = epic.Input.ReviewMeshPath,
            ["EpicReinforceReviewFacePath"] = epic.Input.ReviewFacePath,
            ["SmallReinforceReviewMeshPath"] = small.Input.ReviewMeshPath,
            ["SmallReinforceReviewFacePath"] = small.Input.ReviewFacePath,
            ["CompatibilityPolicy"] = "Existing reinforce token id now denotes the small-ship fore/aft token; reinforce-epic is a new same-face token id.",
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static void SetMeshAsset(JsonObject asset, string id, PromotionResult result, string repository)
    {
        asset["Id"] = id;
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

    private static void SetFaceAsset(JsonObject asset, string id, PromotionResult result, string repository)
    {
        asset["Id"] = id;
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

    private static void SetToken(JsonObject token, string id, string name, string meshAssetId, string faceAssetId, PromotionInput input)
    {
        token["Id"] = id;
        token["Name"] = name;
        token["MeshAssetId"] = meshAssetId;
        token["FaceAssetId"] = faceAssetId;
        token["MeshPath"] = input.CanonicalMeshPath;
        token["FacePath"] = input.CanonicalFacePath;
        token["RuntimeStatus"] = "asset-validation-only";
        token["LuaIncluded"] = false;
    }

    private static void ValidateMesh(string name, ObjInspection reference, ObjInspection review)
    {
        if (review.VertexCount != reference.VertexCount || review.FaceCount != reference.FaceCount)
            throw new InvalidDataException($"{name} mesh vertex or face count changed.");
        if (!reference.Vertices.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(review.Vertices.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException($"{name} vertex positions differ from the validated round mesh.");
        if (!NearlyEqual(reference.Width, review.Width) || !NearlyEqual(reference.Height, review.Height) || !NearlyEqual(reference.Depth, review.Depth))
            throw new InvalidDataException($"{name} physical mesh bounds changed.");
        if (review.UvCount == 0) throw new InvalidDataException($"{name} review mesh has no UV coordinates.");
    }

    private static void ValidateFace(PromotionInput input, ImageInspection face)
    {
        if (face.Width != input.ExpectedWidth || face.Height != input.ExpectedHeight)
            throw new InvalidDataException($"{input.Name} review texture must be {input.ExpectedWidth}x{input.ExpectedHeight}; found {face.Width}x{face.Height}.");
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
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-weapons-disabled-reinforce-tokens <first-edition-repo-folder> [--no-knowledge-base]");

    private sealed record PromotionInput(string Name, string TokenId, string ReferenceMeshPath, string ReviewMeshPath, string ReviewFacePath, string CanonicalMeshPath, string CanonicalFacePath, int ExpectedWidth, int ExpectedHeight);
    private sealed record PromotionResult(PromotionInput Input, ObjInspection Mesh, ImageInspection Face, string MeshHash, string FaceHash);
    private sealed record ObjInspection(int VertexCount, int UvCount, int FaceCount, List<string> Vertices, double Width, double Height, double Depth);
    private sealed record ImageInspection(int Width, int Height, bool HasAlpha);
}
