using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

public static class RegisterFirstEditionTargetLockTokenCommand
{
    private const string LfMeshHash = "c96eedb62bbd66feddd840f58d7dd1dacb456170c1c6fa87890c960b3b0dabb0";
    private const string CrLfMeshHash = "bdeae47c820942318a6e5e2db1d37e80d682a60ebd8752619dc3580c4db2c340";
    private const string FaceHash = "1b4b2474a5d3cbe20dc5c21c5ccaea5968af8664a3b7df6d837ce3018630d63b";
    private const string LuaHash = "90579c7bc2238865a3116ac9f01126d81f1f85c284783c5ea23a6394dc1c96fb";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R19 Target Lock Token Registration");
        Console.WriteLine("==============================================================");
        Console.WriteLine();
        if (args.Length < 1) { ShowUsage(); return 1; }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            RequireDirectory(repository, "Repository");
            var sourceMesh = Resolve(repository, "assets/source/unified25/assets/Items/tokens/squared/TL.obj");
            var sourceFace = Resolve(repository, "assets/source/unified25/assets/Items/tokens/squared/TL.png");
            var canonicalMesh = Resolve(repository, "assets/source/unified1e/gameplay-tokens/meshes/target-lock.obj");
            var canonicalFace = Resolve(repository, "assets/source/unified1e/gameplay-tokens/faces/target-lock.png");
            var manifest = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/core-gameplay-tokens.json");
            var report = Resolve(repository, "_unifiedtoolkit_reports/phase16/target-lock-registration/first-edition-target-lock-registration.json");
            RequireFile(sourceMesh, "Unified 2.5 Target Lock mesh");
            RequireFile(sourceFace, "Unified 2.5 Target Lock texture");
            RequireFile(manifest, "Core gameplay-token manifest");

            var sourceMeshHash = Hash(sourceMesh);
            var sourceFaceHash = Hash(sourceFace);
            if (!sourceMeshHash.Equals(LfMeshHash, StringComparison.OrdinalIgnoreCase) &&
                !sourceMeshHash.Equals(CrLfMeshHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Target Lock mesh hash is not approved: {sourceMeshHash}");
            if (!sourceFaceHash.Equals(FaceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Target Lock texture hash is not approved: {sourceFaceHash}");

            var mesh = InspectObj(sourceMesh);
            if (mesh.VertexCount != 18 || mesh.UvCount != 28 || mesh.FaceCount != 24)
                throw new InvalidDataException($"Target Lock topology is {mesh.VertexCount}/{mesh.UvCount}/{mesh.FaceCount}; expected 18/28/24.");
            var image = InspectImage(sourceFace);
            if (image.Width != 1024 || image.Height != 1024 || !image.HasAlpha)
                throw new InvalidDataException($"Target Lock texture must be 1024x1024 RGBA; found {image.Width}x{image.Height}, alpha={image.HasAlpha}.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalMesh)!);
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalFace)!);
            File.Copy(sourceMesh, canonicalMesh, true);
            File.Copy(sourceFace, canonicalFace, true);
            if (Hash(canonicalMesh) != sourceMeshHash || Hash(canonicalFace) != sourceFaceHash)
                throw new InvalidDataException("Canonical files do not match their approved Unified 2.5 sources.");

            UpdateManifest(manifest, repository, sourceMesh, sourceFace, canonicalMesh, canonicalFace,
                mesh, image, sourceMeshHash, sourceFaceHash);
            WriteReport(report, repository, sourceMesh, sourceFace, canonicalMesh, canonicalFace,
                mesh, image, sourceMeshHash, sourceFaceHash);

            var refresh = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refresh)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                 {repository}");
            Console.WriteLine($"Canonical Target Lock mesh: {canonicalMesh}");
            Console.WriteLine($"Canonical Target Lock face: {canonicalFace}");
            Console.WriteLine($"Texture:                    {image.Width} x {image.Height} RGBA");
            Console.WriteLine($"Geometry:                   {mesh.Width:F6} x {mesh.Height:F6} x {mesh.Depth:F6}");
            Console.WriteLine($"Mesh SHA-256:               {sourceMeshHash}");
            Console.WriteLine($"Face SHA-256:               {sourceFaceHash}");
            Console.WriteLine($"Runtime Lua SHA-256:        {LuaHash}");
            Console.WriteLine("Runtime policy:             reuse existing Unified 2.5 object template");
            Console.WriteLine("Token model:                one owner-labelled token");
            Console.WriteLine($"Manifest:                   {manifest}");
            Console.WriteLine($"Registration report:        {report}");
            Console.WriteLine($"Knowledge base refresh:     {(refresh ? "Yes" : "No")}");
            Console.WriteLine("Images modified:            0");
            Console.WriteLine("Lua scripts copied/modified:0");
            Console.WriteLine();
            Console.WriteLine("Target Lock token registered successfully. Asset files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Target Lock registration failed: {exception.Message}");
            return 1;
        }
    }

    private static void UpdateManifest(string path, string repository, string sourceMesh, string sourceFace,
        string canonicalMesh, string canonicalFace, ObjInfo mesh, ImageInfo image, string meshHash, string faceHash)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = root["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = root["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");
        var meshAsset = FindOrAdd(assets, "target-lock-mesh");
        SetAsset(meshAsset, repository, canonicalMesh, sourceMesh, meshHash, "mesh");
        meshAsset["TokenIds"] = new JsonArray("target-lock");
        meshAsset["Width"] = null; meshAsset["Height"] = null; meshAsset["HasAlpha"] = null;
        meshAsset["MeshBounds"] = new JsonObject
        {
            ["VertexCount"] = mesh.VertexCount, ["Width"] = mesh.Width,
            ["Height"] = mesh.Height, ["Depth"] = mesh.Depth
        };
        var faceAsset = FindOrAdd(assets, "target-lock-face");
        SetAsset(faceAsset, repository, canonicalFace, sourceFace, faceHash, "texture");
        faceAsset["TokenIds"] = new JsonArray("target-lock");
        faceAsset["Width"] = image.Width; faceAsset["Height"] = image.Height;
        faceAsset["HasAlpha"] = image.HasAlpha; faceAsset["MeshBounds"] = null;
        var token = FindOrAdd(tokens, "target-lock");
        token["Name"] = "Target Lock";
        token["MeshAssetId"] = "target-lock-mesh";
        token["FaceAssetId"] = "target-lock-face";
        token["MeshPath"] = Relative(repository, canonicalMesh);
        token["FacePath"] = Relative(repository, canonicalFace);
        token["TokenModel"] = "single-owner-labelled-token";
        token["FirstEditionCompatibilityOverride"] = true;
        token["RuntimeStatus"] = "approved-unified25-runtime-reuse";
        token["LuaIncluded"] = false;
        token["RuntimeBinding"] = Binding();
        root["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        root["AssetCount"] = assets.Count;
        root["TokenCount"] = tokens.Count;
        root["Policy"] = "Original First Edition assets are canonical by default. Target Lock is an explicitly approved Unified 2.5 single-token compatibility reuse. Runtime Lua is referenced from the existing Unified 2.5 object template and is not copied or modified.";
        root["TargetLockRegistration"] = new JsonObject
        {
            ["Status"] = "approved-and-registered",
            ["ArchitectureDecision"] = "reuse-unified25-single-owner-labelled-target-lock",
            ["FirstEditionRulesPolicy"] = "First Edition acquisition, range and spending rules remain authoritative",
            ["AssetCopyPolicy"] = "copy-byte-for-byte-from-repository-unified25-source",
            ["SourceMeshSha256"] = meshHash,
            ["EquivalentLfMeshSha256"] = LfMeshHash,
            ["EquivalentCrLfMeshSha256"] = CrLfMeshHash,
            ["SourceFaceSha256"] = faceHash,
            ["RuntimeBinding"] = Binding(),
            ["LuaCopyPolicy"] = "none-reuse-existing-runtime-template"
        };
        File.WriteAllText(path, root.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static JsonObject Binding() => new()
    {
        ["Strategy"] = "reuse-existing-unified25-target-lock-object-template",
        ["SourceObjectGuid"] = "1670ba",
        ["SourceObjectPath"] = "ObjectStates[192]",
        ["LuaSha256"] = LuaHash,
        ["PreserveOwnerPilotName"] = true,
        ["PreserveOwnerColourTint"] = true,
        ["PreserveTargetAssignmentIntegration"] = true,
        ["PreserveFlipToSpendBehaviour"] = true,
        ["NewLuaRequired"] = false
    };

    private static void SetAsset(JsonObject asset, string repository, string canonical, string source, string hash, string role)
    {
        asset["Role"] = role;
        asset["RepositoryPath"] = Relative(repository, canonical);
        asset["SizeBytes"] = new FileInfo(canonical).Length;
        asset["Sha256"] = hash;
        asset["OriginalSourceUrl"] = role == "mesh"
            ? "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/Items/tokens/squared/TL.obj"
            : "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/Items/tokens/squared/TL.png";
        asset["Resolution"] = "approved-unified25-compatibility-reuse";
        asset["ResolvedFrom"] = Relative(repository, source);
        asset["SourceKind"] = "unified25-approved-runtime-reuse";
    }

    private static void WriteReport(string path, string repository, string sourceMesh, string sourceFace,
        string canonicalMesh, string canonicalFace, ObjInfo mesh, ImageInfo image, string meshHash, string faceHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var report = new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["Decision"] = "Approved Unified 2.5 single owner-labelled Target Lock compatibility reuse",
            ["SourceMeshPath"] = Relative(repository, sourceMesh),
            ["SourceFacePath"] = Relative(repository, sourceFace),
            ["CanonicalMeshPath"] = Relative(repository, canonicalMesh),
            ["CanonicalFacePath"] = Relative(repository, canonicalFace),
            ["MeshSha256"] = meshHash,
            ["FaceSha256"] = faceHash,
            ["Texture"] = new JsonObject { ["Width"] = image.Width, ["Height"] = image.Height, ["HasAlpha"] = image.HasAlpha },
            ["Mesh"] = new JsonObject
            {
                ["VertexCount"] = mesh.VertexCount, ["UvCount"] = mesh.UvCount, ["FaceCount"] = mesh.FaceCount,
                ["Width"] = mesh.Width, ["Height"] = mesh.Height, ["Depth"] = mesh.Depth
            },
            ["RuntimeBinding"] = Binding(),
            ["AssetFilesCopiedByteForByte"] = true,
            ["ImagesModified"] = 0,
            ["LuaScriptsCopiedOrModified"] = 0
        };
        File.WriteAllText(path, report.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static JsonObject FindOrAdd(JsonArray items, string id)
    {
        var found = items.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;
        var created = new JsonObject { ["Id"] = id };
        items.Add(created);
        return created;
    }

    private static ObjInfo InspectObj(string path)
    {
        var points = new List<(double X, double Y, double Z)>(); var uv = 0; var faces = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("vt ")) { uv++; continue; }
            if (line.StartsWith("f ")) { faces++; continue; }
            if (!line.StartsWith("v ")) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException($"Unreadable OBJ vertex: {line}");
            points.Add((x, y, z));
        }
        if (points.Count == 0) throw new InvalidDataException($"OBJ contains no vertices: {path}");
        return new ObjInfo(points.Count, uv, faces,
            points.Max(p => p.X) - points.Min(p => p.X),
            points.Max(p => p.Y) - points.Min(p => p.Y),
            points.Max(p => p.Z) - points.Min(p => p.Z));
    }

    private static ImageInfo InspectImage(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInfo(bitmap.Width, bitmap.Height, bitmap.AlphaType != SKAlphaType.Opaque);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: register-first-edition-target-lock-token <first-edition-repo-folder> [--no-knowledge-base]");
    private sealed record ObjInfo(int VertexCount, int UvCount, int FaceCount, double Width, double Height, double Depth);
    private sealed record ImageInfo(int Width, int Height, bool HasAlpha);
}
