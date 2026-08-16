using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

public static class PromoteFirstEditionCloakEnergyTokensCommand
{
    private const string ApprovedCloakMeshHash = "f9a8167a96a610866d33c55ffcedaf2f2fe5fc76c10a873fb4d5ed5e6b1cdbfb";
    private const string ApprovedCloakFaceHash = "ddd47201a1a516741b0c84724ae9a41188e49a32b644da48a96eec44377c00f5";
    private const string ApprovedEnergyMeshHash = "7ae5ce581c13ab77fc0b2dc916646bbd45b8e950e0e0cd4c56f3128781c81d5b";
    private const string ApprovedEnergyFaceHash = "27ca912fb89d4d634e6fac5499e2cc3aec396b0b47ba523b7ee21e9d767f2a61";
    private const string AssetBaseUrl = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R16 Cloak and Energy Token Promotion");
        Console.WriteLine("================================================================");
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
            var cloak = ResolveTokenFiles(repository, "cloak", "cloak_test_v3.obj", "cloak_test.png");
            var energy = ResolveTokenFiles(repository, "energy", "energy_test_r8.obj", "energy_test.png");

            RequireFile(manifestPath, "Core gameplay-token manifest");
            ValidateInput(cloak, ApprovedCloakMeshHash, ApprovedCloakFaceHash, 48, 144, 68, 480, 480);
            ValidateInput(energy, ApprovedEnergyMeshHash, ApprovedEnergyFaceHash, 86, 258, 125, 840, 420);

            Directory.CreateDirectory(Path.GetDirectoryName(cloak.CanonicalMesh)!);
            Directory.CreateDirectory(Path.GetDirectoryName(cloak.CanonicalFace)!);
            File.Copy(cloak.ReviewMesh, cloak.CanonicalMesh, true);
            File.Copy(cloak.ReviewFace, cloak.CanonicalFace, true);
            File.Copy(energy.ReviewMesh, energy.CanonicalMesh, true);
            File.Copy(energy.ReviewFace, energy.CanonicalFace, true);

            var cloakResult = InspectPromoted(cloak);
            var energyResult = InspectPromoted(energy);
            UpdateManifest(manifestPath, repository, cloak, cloakResult, energy, energyResult);

            var validationSave = WriteValidationSave(repository, cloak, energy);
            var refresh = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refresh)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0)
                    throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:              {repository}");
            PrintResult("Cloak", cloak, cloakResult);
            PrintResult("Energy", energy, energyResult);
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"Validation save:         {validationSave}");
            Console.WriteLine($"Knowledge base refresh:  {(refresh ? "Yes" : "No")}");
            Console.WriteLine("Artwork processing:      none; files copied byte-for-byte");
            Console.WriteLine("Images modified:         0");
            Console.WriteLine("Lua scripts modified:    0");
            Console.WriteLine();
            Console.WriteLine("Cloak and Energy tokens promoted successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Cloak and Energy promotion failed: {exception.Message}");
            return 1;
        }
    }

    private static TokenFiles ResolveTokenFiles(string repository, string id, string reviewMeshName, string reviewFaceName) => new(
        id,
        Resolve(repository, $"assets/source/unified1e/gameplay-tokens/review/{reviewMeshName}"),
        Resolve(repository, $"assets/source/unified1e/gameplay-tokens/review/{reviewFaceName}"),
        Resolve(repository, $"assets/source/unified1e/gameplay-tokens/meshes/{id}.obj"),
        Resolve(repository, $"assets/source/unified1e/gameplay-tokens/faces/{id}.png"));

    private static void ValidateInput(TokenFiles files, string meshHash, string faceHash,
        int vertices, int uvs, int faces, int width, int height)
    {
        RequireFile(files.ReviewMesh, $"Approved {files.Id} review mesh");
        RequireFile(files.ReviewFace, $"Approved {files.Id} review face");

        var actualMeshHash = HashFile(files.ReviewMesh);
        var actualFaceHash = HashFile(files.ReviewFace);
        if (!actualMeshHash.Equals(meshHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{files.Id} review mesh does not match the approved revision.");
        if (!actualFaceHash.Equals(faceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{files.Id} review face does not match the approved scan.");

        var mesh = InspectObj(files.ReviewMesh);
        if (mesh.VertexCount != vertices || mesh.UvCount != uvs || mesh.FaceCount != faces)
            throw new InvalidDataException($"{files.Id} OBJ topology is {mesh.VertexCount}/{mesh.UvCount}/{mesh.FaceCount}; expected {vertices}/{uvs}/{faces} vertices/UVs/faces.");

        var image = InspectImage(files.ReviewFace);
        if (image.Width != width || image.Height != height)
            throw new InvalidDataException($"{files.Id} texture is {image.Width}x{image.Height}; expected {width}x{height}.");
    }

    private static PromotionResult InspectPromoted(TokenFiles files)
    {
        var meshHash = HashFile(files.CanonicalMesh);
        var faceHash = HashFile(files.CanonicalFace);
        if (!meshHash.Equals(HashFile(files.ReviewMesh), StringComparison.OrdinalIgnoreCase) ||
            !faceHash.Equals(HashFile(files.ReviewFace), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Canonical {files.Id} files do not match their approved review inputs.");
        return new PromotionResult(InspectObj(files.CanonicalMesh), InspectImage(files.CanonicalFace), meshHash, faceHash);
    }

    private static void UpdateManifest(string path, string repository, TokenFiles cloak, PromotionResult cloakResult,
        TokenFiles energy, PromotionResult energyResult)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException("Could not parse core gameplay-token manifest.");
        var assets = root["Assets"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Assets.");
        var tokens = root["Tokens"]?.AsArray() ?? throw new InvalidDataException("Manifest does not contain Tokens.");

        UpsertAsset(assets, repository, cloak, cloakResult, "cloak");
        UpsertAsset(assets, repository, energy, energyResult, "energy");
        UpsertToken(tokens, repository, cloak, "Cloak", "same-artwork-both-sides");
        UpsertToken(tokens, repository, energy, "Energy", "active-pink-front-spent-grey-reverse");

        root["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        root["AssetCount"] = assets.Count;
        root["TokenCount"] = tokens.Count;
        root["Policy"] = "Original First Edition assets promoted byte-for-byte. Approved scans use validated UV-remapped meshes. Runtime Lua is unchanged.";
        root["CloakEnergyPromotion"] = new JsonObject
        {
            ["Status"] = "approved-and-promoted",
            ["CloakReviewMeshPath"] = Relative(repository, cloak.ReviewMesh),
            ["CloakReviewFacePath"] = Relative(repository, cloak.ReviewFace),
            ["CloakGeometryPolicy"] = "approved-r3-custom-silhouette-black-edges",
            ["EnergyReviewMeshPath"] = Relative(repository, energy.ReviewMesh),
            ["EnergyReviewFacePath"] = Relative(repository, energy.ReviewFace),
            ["EnergyGeometryPolicy"] = "approved-r8-shared-conservative-silhouette-vertical-black-walls",
            ["EnergyFacePolicy"] = "active-pink-front-spent-grey-reverse",
            ["ArtworkProcessing"] = "none-copy-byte-for-byte",
            ["VisualValidation"] = "approved-in-tabletop-simulator",
            ["LuaPolicy"] = "unchanged-runtime-behaviour-deferred"
        };

        File.WriteAllText(path, root.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static void UpsertAsset(JsonArray assets, string repository, TokenFiles files, PromotionResult result, string tokenId)
    {
        var mesh = FindOrAdd(assets, $"{tokenId}-mesh");
        mesh["TokenIds"] = new JsonArray(tokenId);
        mesh["Role"] = "mesh";
        mesh["RepositoryPath"] = Relative(repository, files.CanonicalMesh);
        mesh["SizeBytes"] = new FileInfo(files.CanonicalMesh).Length;
        mesh["Sha256"] = result.MeshHash;
        mesh["OriginalSourceUrl"] = null;
        mesh["Resolution"] = "approved-review-promotion";
        mesh["ResolvedFrom"] = Relative(repository, files.ReviewMesh);
        mesh["Width"] = null;
        mesh["Height"] = null;
        mesh["HasAlpha"] = null;
        mesh["MeshBounds"] = new JsonObject
        {
            ["VertexCount"] = result.Mesh.VertexCount,
            ["Width"] = result.Mesh.Width,
            ["Height"] = result.Mesh.Height,
            ["Depth"] = result.Mesh.Depth
        };
        mesh["SourceKind"] = "uv-remapped-validated-mesh";

        var face = FindOrAdd(assets, $"{tokenId}-face");
        face["TokenIds"] = new JsonArray(tokenId);
        face["Role"] = "texture";
        face["RepositoryPath"] = Relative(repository, files.CanonicalFace);
        face["SizeBytes"] = new FileInfo(files.CanonicalFace).Length;
        face["Sha256"] = result.FaceHash;
        face["OriginalSourceUrl"] = null;
        face["Resolution"] = "approved-review-promotion";
        face["ResolvedFrom"] = Relative(repository, files.ReviewFace);
        face["Width"] = result.Image.Width;
        face["Height"] = result.Image.Height;
        face["HasAlpha"] = result.Image.HasAlpha;
        face["MeshBounds"] = null;
        face["SourceKind"] = "user-supplied-high-resolution-scan";
    }

    private static void UpsertToken(JsonArray tokens, string repository, TokenFiles files, string name, string sidePolicy)
    {
        var token = FindOrAdd(tokens, files.Id);
        token["Name"] = name;
        token["MeshAssetId"] = $"{files.Id}-mesh";
        token["FaceAssetId"] = $"{files.Id}-face";
        token["MeshPath"] = Relative(repository, files.CanonicalMesh);
        token["FacePath"] = Relative(repository, files.CanonicalFace);
        token["SidePolicy"] = sidePolicy;
        token["RuntimeStatus"] = "asset-validation-only";
        token["LuaIncluded"] = false;
    }

    private static string WriteValidationSave(string repository, TokenFiles cloak, TokenFiles energy)
    {
        var output = Resolve(repository, "_unifiedtoolkit_reports/phase16/cloak-energy-token-promotion/first-edition-cloak-energy-token-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var objects = new JsonArray
        {
            BuildValidationObject("c10a01", "Cloak — side A", cloak, -4.5, 0),
            BuildValidationObject("c10b01", "Cloak — flipped", cloak, -1.5, 180),
            BuildValidationObject("e10a01", "Energy — active", energy, 1.5, 0),
            BuildValidationObject("e10b01", "Energy — spent", energy, 4.5, 180)
        };
        var save = new JsonObject
        {
            ["SaveName"] = "First Edition Cloak and Energy Token Validation",
            ["EpochTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["Date"] = DateTimeOffset.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
            ["VersionNumber"] = "v14.2.1",
            ["GameMode"] = "",
            ["GameType"] = "",
            ["GameComplexity"] = "",
            ["Tags"] = new JsonArray(),
            ["Gravity"] = 0.5,
            ["PlayArea"] = 0.5,
            ["Table"] = "Table_None",
            ["Sky"] = "Sky_Museum",
            ["Note"] = "Phase 16E-R16 canonical Cloak and Energy token validation. Objects are unlocked and manually flippable.",
            ["Grid"] = new JsonObject { ["Type"] = 0, ["Lines"] = false, ["Snapping"] = false },
            ["Hands"] = new JsonObject { ["Enable"] = false, ["DisableUnused"] = true },
            ["ObjectStates"] = objects,
            ["LuaScript"] = "",
            ["LuaScriptState"] = "",
            ["XmlUI"] = ""
        };
        File.WriteAllText(output, save.ToJsonString(JsonOptions), new UTF8Encoding(false));
        return output;
    }

    private static JsonObject BuildValidationObject(string guid, string nickname, TokenFiles files, double x, double rotZ) => new()
    {
        ["GUID"] = guid,
        ["Name"] = "Custom_Model",
        ["Transform"] = new JsonObject
        {
            ["posX"] = x, ["posY"] = 1.1, ["posZ"] = 0.0,
            ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = rotZ,
            ["scaleX"] = 0.72, ["scaleY"] = 0.72, ["scaleZ"] = 0.72
        },
        ["Nickname"] = nickname,
        ["Description"] = "Canonical First Edition gameplay token; manually flippable validation object.",
        ["GMNotes"] = "",
        ["AltLookAngle"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 },
        ["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 },
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
            ["MeshURL"] = AssetBaseUrl + files.CanonicalMeshRelative,
            ["DiffuseURL"] = AssetBaseUrl + files.CanonicalFaceRelative,
            ["NormalURL"] = "",
            ["ColliderURL"] = "",
            ["Convex"] = true,
            ["MaterialIndex"] = 3,
            ["TypeIndex"] = 5,
            ["CastShadows"] = true
        },
        ["LuaScript"] = "",
        ["LuaScriptState"] = "",
        ["XmlUI"] = ""
    };

    private static void PrintResult(string label, TokenFiles files, PromotionResult result)
    {
        Console.WriteLine($"{label} mesh:             {files.CanonicalMesh}");
        Console.WriteLine($"{label} face:             {files.CanonicalFace}");
        Console.WriteLine($"{label} texture:          {result.Image.Width} x {result.Image.Height}");
        Console.WriteLine($"{label} geometry:         {result.Mesh.Width:F4} x {result.Mesh.Height:F4} x {result.Mesh.Depth:F4}");
        Console.WriteLine($"{label} mesh SHA-256:     {result.MeshHash}");
        Console.WriteLine($"{label} face SHA-256:     {result.FaceHash}");
    }

    private static JsonObject FindOrAdd(JsonArray items, string id)
    {
        var existing = items.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["Id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;
        var created = new JsonObject { ["Id"] = id };
        items.Add(created);
        return created;
    }

    private static ObjInfo InspectObj(string path)
    {
        var points = new List<(double X, double Y, double Z)>();
        var uv = 0;
        var faces = 0;
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
        if (points.Count == 0)
            throw new InvalidDataException($"OBJ contains no vertices: {path}");
        return new ObjInfo(points.Count, uv, faces,
            points.Max(point => point.X) - points.Min(point => point.X),
            points.Max(point => point.Y) - points.Min(point => point.Y),
            points.Max(point => point.Z) - points.Min(point => point.Z));
    }

    private static ImageInfo InspectImage(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode PNG: {path}");
        return new ImageInfo(bitmap.Width, bitmap.Height, bitmap.AlphaType != SKAlphaType.Opaque);
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: promote-first-edition-cloak-energy-tokens <first-edition-repo-folder> [--no-knowledge-base]");

    private sealed record TokenFiles(string Id, string ReviewMesh, string ReviewFace, string CanonicalMesh, string CanonicalFace)
    {
        public string CanonicalMeshRelative => $"assets/source/unified1e/gameplay-tokens/meshes/{Id}.obj";
        public string CanonicalFaceRelative => $"assets/source/unified1e/gameplay-tokens/faces/{Id}.png";
    }

    private sealed record ObjInfo(int VertexCount, int UvCount, int FaceCount, double Width, double Height, double Depth);
    private sealed record ImageInfo(int Width, int Height, bool HasAlpha);
    private sealed record PromotionResult(ObjInfo Mesh, ImageInfo Image, string MeshHash, string FaceHash);
}
