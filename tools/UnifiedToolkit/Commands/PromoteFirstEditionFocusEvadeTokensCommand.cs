using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R10 promotes the approved high-resolution Focus and Evade scans
/// with their R2 UV-remapped meshes. Focus is deliberately split from the
/// mesh shared with Weapons Disabled so that token's UV mapping is preserved.
/// </summary>
public static class PromoteFirstEditionFocusEvadeTokensCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R10 High-Resolution Focus and Evade Promotion");
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

            var sharedFocusMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/round-focus-weapons-disabled.obj");
            var reviewFocusMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/focus_test_v2.obj");
            var reviewFocusFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/focus_test.png");
            var canonicalFocusMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/focus.obj");
            var canonicalFocusFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/focus.png");

            var reviewEvadeMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/evade_test_v2.obj");
            var reviewEvadeFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/review/evade_test.png");
            var canonicalEvadeMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/evade.obj");
            var canonicalEvadeFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/evade.png");
            var manifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/core-gameplay-tokens.json");

            foreach (var required in new[]
            {
                sharedFocusMesh, reviewFocusMesh, reviewFocusFace, canonicalFocusFace,
                reviewEvadeMesh, reviewEvadeFace, canonicalEvadeMesh, canonicalEvadeFace, manifestPath
            })
                RequireFile(required, "Required promotion input");

            var oldFocusMesh = InspectObj(sharedFocusMesh);
            var newFocusMesh = InspectObj(reviewFocusMesh);
            var oldEvadeMesh = InspectObj(canonicalEvadeMesh);
            var newEvadeMesh = InspectObj(reviewEvadeMesh);
            ValidateMesh("Focus", oldFocusMesh, newFocusMesh);
            ValidateMesh("Evade", oldEvadeMesh, newEvadeMesh);

            var oldFocusFace = InspectImage(canonicalFocusFace);
            var newFocusFace = InspectImage(reviewFocusFace);
            var oldEvadeFace = InspectImage(canonicalEvadeFace);
            var newEvadeFace = InspectImage(reviewEvadeFace);
            ValidateFace("Focus", oldFocusFace, newFocusFace);
            ValidateFace("Evade", oldEvadeFace, newEvadeFace);

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalFocusMesh)!);
            File.Copy(reviewFocusMesh, canonicalFocusMesh, true);
            File.Copy(reviewFocusFace, canonicalFocusFace, true);
            File.Copy(reviewEvadeMesh, canonicalEvadeMesh, true);
            File.Copy(reviewEvadeFace, canonicalEvadeFace, true);

            var focusMeshHash = HashFile(canonicalFocusMesh);
            var focusFaceHash = HashFile(canonicalFocusFace);
            var evadeMeshHash = HashFile(canonicalEvadeMesh);
            var evadeFaceHash = HashFile(canonicalEvadeFace);
            UpdateManifest(
                manifestPath,
                repository,
                sharedFocusMesh,
                reviewFocusMesh,
                reviewFocusFace,
                canonicalFocusMesh,
                canonicalFocusFace,
                reviewEvadeMesh,
                reviewEvadeFace,
                canonicalEvadeMesh,
                canonicalEvadeFace,
                newFocusMesh,
                newFocusFace,
                newEvadeMesh,
                newEvadeFace,
                focusMeshHash,
                focusFaceHash,
                evadeMeshHash,
                evadeFaceHash);

            var refreshKnowledgeBase = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refreshKnowledgeBase)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                 {repository}");
            Console.WriteLine($"Focus canonical mesh:       {canonicalFocusMesh}");
            Console.WriteLine($"Focus canonical face:       {canonicalFocusFace}");
            Console.WriteLine($"Evade canonical mesh:       {canonicalEvadeMesh}");
            Console.WriteLine($"Evade canonical face:       {canonicalEvadeFace}");
            Console.WriteLine($"Focus texture:              {newFocusFace.Width} x {newFocusFace.Height}");
            Console.WriteLine($"Evade texture:              {newEvadeFace.Width} x {newEvadeFace.Height}");
            Console.WriteLine($"Focus mesh SHA-256:         {focusMeshHash}");
            Console.WriteLine($"Focus face SHA-256:         {focusFaceHash}");
            Console.WriteLine($"Evade mesh SHA-256:         {evadeMeshHash}");
            Console.WriteLine($"Evade face SHA-256:         {evadeFaceHash}");
            Console.WriteLine($"Shared Weapons mesh intact: {HashFile(sharedFocusMesh) == oldFocusMesh.Sha256}");
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine($"Knowledge base refresh:     {(refreshKnowledgeBase ? "Yes" : "No")}");
            Console.WriteLine($"Images modified:            0");
            Console.WriteLine($"Lua scripts modified:       0");
            Console.WriteLine();
            Console.WriteLine("High-resolution Focus and Evade tokens promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Focus and Evade promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateManifest(
        string manifestPath,
        string repository,
        string sharedFocusMesh,
        string reviewFocusMesh,
        string reviewFocusFace,
        string canonicalFocusMesh,
        string canonicalFocusFace,
        string reviewEvadeMesh,
        string reviewEvadeFace,
        string canonicalEvadeMesh,
        string canonicalEvadeFace,
        ObjInspection focusMesh,
        ImageInspection focusFace,
        ObjInspection evadeMesh,
        ImageInspection evadeFace,
        string focusMeshHash,
        string focusFaceHash,
        string evadeMeshHash,
        string evadeFaceHash)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ??
            throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = manifest["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = manifest["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");

        var sharedAsset = FindAsset(assets, "round-focus-weapons-disabled-mesh");
        sharedAsset["TokenIds"] = new JsonArray("weapons-disabled");

        var focusMeshAsset = assets.OfType<JsonObject>().FirstOrDefault(asset =>
            string.Equals(asset["Id"]?.GetValue<string>(), "focus-mesh", StringComparison.OrdinalIgnoreCase));
        if (focusMeshAsset is null)
        {
            focusMeshAsset = new JsonObject();
            assets.Add(focusMeshAsset);
        }
        SetMeshAsset(focusMeshAsset, "focus-mesh", "focus", canonicalFocusMesh, reviewFocusMesh, repository, focusMesh, focusMeshHash);

        SetFaceAsset(FindAsset(assets, "focus-face"), canonicalFocusFace, reviewFocusFace, repository, focusFace, focusFaceHash);
        SetMeshAsset(FindAsset(assets, "evade-mesh"), "evade-mesh", "evade", canonicalEvadeMesh, reviewEvadeMesh, repository, evadeMesh, evadeMeshHash);
        SetFaceAsset(FindAsset(assets, "evade-face"), canonicalEvadeFace, reviewEvadeFace, repository, evadeFace, evadeFaceHash);

        var focusToken = FindToken(tokens, "focus");
        focusToken["MeshAssetId"] = "focus-mesh";
        focusToken["MeshPath"] = Relative(repository, canonicalFocusMesh);

        manifest["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        manifest["AssetCount"] = assets.Count;
        manifest["Policy"] = "Original First Edition assets promoted byte-for-byte. Shield, Focus and Evade use approved high-resolution physical scans with validated UV-remapped meshes. Runtime Lua is unchanged.";
        manifest["FocusEvadePromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["FocusReviewMeshPath"] = Relative(repository, reviewFocusMesh),
            ["FocusReviewFacePath"] = Relative(repository, reviewFocusFace),
            ["EvadeReviewMeshPath"] = Relative(repository, reviewEvadeMesh),
            ["EvadeReviewFacePath"] = Relative(repository, reviewEvadeFace),
            ["SharedWeaponsDisabledMeshPath"] = Relative(repository, sharedFocusMesh),
            ["SharedMeshPolicy"] = "Focus split to a dedicated mesh; Weapons Disabled remains on the original shared mesh.",
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static void SetMeshAsset(JsonObject asset, string id, string tokenId, string canonical, string review, string repository, ObjInspection mesh, string hash)
    {
        asset["Id"] = id;
        asset["TokenIds"] = new JsonArray(tokenId);
        asset["Role"] = "mesh";
        SetCommon(asset, canonical, review, repository, hash);
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

    private static void SetFaceAsset(JsonObject asset, string canonical, string review, string repository, ImageInspection image, string hash)
    {
        SetCommon(asset, canonical, review, repository, hash);
        asset["Width"] = image.Width;
        asset["Height"] = image.Height;
        asset["HasAlpha"] = image.HasAlpha;
        asset["MeshBounds"] = null;
    }

    private static void SetCommon(JsonObject asset, string canonical, string review, string repository, string hash)
    {
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
            throw new InvalidDataException($"{name} vertex positions differ from the validated mesh.");
        if (!NearlyEqual(original.Width, review.Width) || !NearlyEqual(original.Height, review.Height) || !NearlyEqual(original.Depth, review.Depth))
            throw new InvalidDataException($"{name} physical mesh bounds changed.");
        if (review.UvCount == 0) throw new InvalidDataException($"{name} review mesh has no UV coordinates.");
    }

    private static void ValidateFace(string name, ImageInspection original, ImageInspection review)
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
            Sha256 = HashFile(path),
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
    private static JsonObject FindToken(JsonArray tokens, string id) => tokens.OfType<JsonObject>().FirstOrDefault(token =>
        string.Equals(token["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidDataException($"Manifest token not found: {id}");
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-focus-evade-tokens <first-edition-repo-folder> [--no-knowledge-base]");

    private sealed class ObjInspection
    {
        public string Sha256 { get; init; } = "";
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
