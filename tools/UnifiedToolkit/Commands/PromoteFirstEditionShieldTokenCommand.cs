using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Promotes the visually approved high-resolution Shield texture and its
/// Blender-remapped OBJ from review paths into the canonical First Edition
/// gameplay-token paths. Artwork is copied byte-for-byte; it is never altered.
/// </summary>
public static class PromoteFirstEditionShieldTokenCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R9 High-Resolution Shield Promotion");
        Console.WriteLine("==============================================================");
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

            var reviewMesh = Resolve(repository, Option(args, "--mesh") ??
                "assets/source/unified1e/gameplay-tokens/review/shield_test.obj");
            var reviewFace = Resolve(repository, Option(args, "--face") ??
                "assets/source/unified1e/gameplay-tokens/review/shield_test.png");
            var workingSource = Resolve(repository, Option(args, "--working-source") ??
                "assets/source/unified1e/gameplay-tokens/working/shield_test.blend");
            var canonicalMesh = Resolve(repository,
                "assets/source/unified1e/gameplay-tokens/meshes/shield.obj");
            var canonicalFace = Resolve(repository,
                "assets/source/unified1e/gameplay-tokens/faces/shield.png");
            var manifestPath = Resolve(repository,
                "assets/source/unified1e/reference/gameplay-objects/core-gameplay-tokens.json");

            RequireFile(reviewMesh, "Review Shield mesh");
            RequireFile(reviewFace, "Review Shield face");
            RequireFile(workingSource, "Blender working source");
            RequireFile(canonicalMesh, "Canonical Shield mesh");
            RequireFile(canonicalFace, "Canonical Shield face");
            RequireFile(manifestPath, "Core gameplay-token manifest");

            var oldMesh = InspectObj(canonicalMesh);
            var newMesh = InspectObj(reviewMesh);
            ValidateMesh(oldMesh, newMesh);

            var oldFace = InspectImage(canonicalFace);
            var newFace = InspectImage(reviewFace);
            ValidateFace(oldFace, newFace);

            File.Copy(reviewMesh, canonicalMesh, true);
            File.Copy(reviewFace, canonicalFace, true);

            var meshHash = HashFile(canonicalMesh);
            var faceHash = HashFile(canonicalFace);
            UpdateManifest(
                manifestPath,
                repository,
                canonicalMesh,
                canonicalFace,
                reviewMesh,
                reviewFace,
                workingSource,
                newMesh,
                newFace,
                meshHash,
                faceHash);

            var refreshKnowledgeBase = !args.Any(value =>
                value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            var knowledgeBaseResult = 0;
            if (refreshKnowledgeBase)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                knowledgeBaseResult = BuildKnowledgeBaseCommand.Run([repository]);
                if (knowledgeBaseResult != 0)
                    throw new InvalidOperationException($"Knowledge-base refresh returned exit code {knowledgeBaseResult}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:             {repository}");
            Console.WriteLine($"Review mesh:            {reviewMesh}");
            Console.WriteLine($"Review face:            {reviewFace}");
            Console.WriteLine($"Working source:         {workingSource}");
            Console.WriteLine($"Vertices:               {newMesh.VertexCount}");
            Console.WriteLine($"Faces:                  {newMesh.FaceCount}");
            Console.WriteLine($"Dimensions:             {Format(newMesh.Width)} x {Format(newMesh.Height)} x {Format(newMesh.Depth)}");
            Console.WriteLine($"Texture:                {newFace.Width} x {newFace.Height}");
            Console.WriteLine($"Mesh SHA-256:           {meshHash}");
            Console.WriteLine($"Face SHA-256:           {faceHash}");
            Console.WriteLine($"Manifest:               {manifestPath}");
            Console.WriteLine($"Knowledge base refresh: {(refreshKnowledgeBase ? "Yes" : "No")}");
            Console.WriteLine($"Images modified:        0");
            Console.WriteLine($"Lua scripts modified:   0");
            Console.WriteLine();
            Console.WriteLine("High-resolution Shield token promoted successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Shield token promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static void ValidateMesh(ObjInspection oldMesh, ObjInspection newMesh)
    {
        if (newMesh.VertexCount != oldMesh.VertexCount)
            throw new InvalidDataException($"Vertex count changed from {oldMesh.VertexCount} to {newMesh.VertexCount}.");
        if (newMesh.FaceCount != oldMesh.FaceCount)
            throw new InvalidDataException($"Face count changed from {oldMesh.FaceCount} to {newMesh.FaceCount}.");
        if (!oldMesh.Vertices.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(newMesh.Vertices.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Vertex positions differ from the validated canonical Shield mesh.");
        if (!NearlyEqual(oldMesh.Width, newMesh.Width) ||
            !NearlyEqual(oldMesh.Height, newMesh.Height) ||
            !NearlyEqual(oldMesh.Depth, newMesh.Depth))
            throw new InvalidDataException("Physical mesh bounds differ from the validated canonical Shield mesh.");
        if (newMesh.UvCount == 0)
            throw new InvalidDataException("Review OBJ contains no UV coordinates.");
    }

    private static void ValidateFace(ImageInspection oldFace, ImageInspection newFace)
    {
        if (newFace.Width < oldFace.Width || newFace.Height < oldFace.Height)
            throw new InvalidDataException("Review texture is lower resolution than the canonical Shield texture.");
        if (newFace.Width != newFace.Height * 2)
            throw new InvalidDataException($"Review texture must have a 2:1 two-face layout; found {newFace.Width}x{newFace.Height}.");
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
            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                uvCount++;
                continue;
            }
            if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                faceCount++;
                continue;
            }
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
        using var bitmap = SKBitmap.Decode(path) ??
            throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInspection
        {
            Width = bitmap.Width,
            Height = bitmap.Height,
            HasAlpha = bitmap.AlphaType != SKAlphaType.Opaque
        };
    }

    private static void UpdateManifest(
        string manifestPath,
        string repository,
        string canonicalMesh,
        string canonicalFace,
        string reviewMesh,
        string reviewFace,
        string workingSource,
        ObjInspection mesh,
        ImageInspection face,
        string meshHash,
        string faceHash)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ??
            throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = manifest["Assets"]?.AsArray() ??
            throw new InvalidDataException("Manifest does not contain Assets.");
        var meshAsset = FindAsset(assets, "shield-mesh");
        var faceAsset = FindAsset(assets, "shield-face");

        UpdateCommon(meshAsset, canonicalMesh, reviewMesh, repository, meshHash);
        meshAsset["MeshBounds"] = new JsonObject
        {
            ["VertexCount"] = mesh.VertexCount,
            ["Width"] = mesh.Width,
            ["Height"] = mesh.Height,
            ["Depth"] = mesh.Depth
        };

        UpdateCommon(faceAsset, canonicalFace, reviewFace, repository, faceHash);
        faceAsset["Width"] = face.Width;
        faceAsset["Height"] = face.Height;
        faceAsset["HasAlpha"] = face.HasAlpha;

        manifest["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        manifest["Policy"] = "Original First Edition assets promoted byte-for-byte. Shield artwork uses the approved high-resolution physical scan and Blender-remapped UV mesh. Runtime Lua and gameplay registries are unchanged.";
        manifest["ShieldPromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["ReviewMeshPath"] = Relative(repository, reviewMesh),
            ["ReviewFacePath"] = Relative(repository, reviewFace),
            ["WorkingSourcePath"] = Relative(repository, workingSource),
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator"
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static JsonObject FindAsset(JsonArray assets, string id)
    {
        return assets.OfType<JsonObject>().FirstOrDefault(asset =>
            string.Equals(asset["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException($"Manifest asset not found: {id}");
    }

    private static void UpdateCommon(JsonObject asset, string canonical, string review, string repository, string hash)
    {
        asset["RepositoryPath"] = Relative(repository, canonical);
        asset["SizeBytes"] = new FileInfo(canonical).Length;
        asset["Sha256"] = hash;
        asset["Resolution"] = "approved-review-promotion";
        asset["ResolvedFrom"] = Relative(repository, review);
        asset["OriginalSourceUrl"] = null;
        asset["SourceKind"] = "user-supplied-high-resolution-scan";
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;
    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Resolve(string repository, string path) => Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) =>
        Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}");
    }
    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path);
    }
    private static void ShowUsage() => Console.WriteLine(
        "Usage: promote-first-edition-shield-token <first-edition-repo-folder> [--mesh <file>] [--face <file>] [--working-source <file>] [--no-knowledge-base]");

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
