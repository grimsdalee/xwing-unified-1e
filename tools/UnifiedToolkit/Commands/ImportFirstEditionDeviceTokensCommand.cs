using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Promotes the nine visually approved First Edition device definitions from
/// repository-owned Unified 2.5 sources. This is asset and semantic-manifest
/// registration only; no runtime Lua or gameplay registry is changed.
/// </summary>
public static class ImportFirstEditionDeviceTokensCommand
{
    private const string Unified25Root = "assets/source/unified25/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly AssetSeed[] Assets =
    [
        new("device-bomb-mesh", "mesh", "assets/colliders/bomb_collider.obj", "meshes/bomb.obj"),
        new("device-bomb-collider", "collider", "assets/Items/arcranges/new/bomb_collider.obj", "colliders/bomb.obj"),
        new("bomblet-face", "texture", "assets/textures/bombs/bomblet-texture.png", "faces/bomblet.png"),
        new("ion-bomb-face", "texture", "assets/textures/bombs/ionbomb.png", "faces/ion-bomb.png"),
        new("proton-bomb-face", "texture", "assets/textures/bombs/proton-bomb-texture.png", "faces/proton-bomb.png"),
        new("seismic-charge-face", "texture", "assets/textures/bombs/seismic-charge-texture.png", "faces/seismic-charge.png"),
        new("thermal-detonator-face", "texture", "assets/textures/bombs/thermalbomb.png", "faces/thermal-detonator.png"),
        new("proximity-mine-mesh", "mesh", "assets/Items/tokens/devices/proximity.obj", "meshes/proximity-mine.obj"),
        new("proximity-mine-collider", "collider", "assets/Items/tokens/devices/proximity-col.obj", "colliders/proximity-mine.obj"),
        new("proximity-mine-face", "texture", "assets/Items/tokens/devices/proximity.png", "faces/proximity-mine.png"),
        new("cluster-mine-centre-face", "texture", "assets/textures/bombs/clustermine.png", "faces/cluster-mine-centre.png"),
        new("cluster-mine-side-face", "texture", "assets/textures/bombs/clustermine-side.png", "faces/cluster-mine-side.png"),
        new("conner-net-face", "texture", "assets/textures/bombs/connor-net-image.png", "faces/conner-net.png"),
        new("rigged-cargo-face", "texture", "assets/HotAC/objectives/loose-cargo-image.png", "faces/rigged-cargo.png")
    ];

    private static readonly DeviceSeed[] Devices =
    [
        Bomb("seismic-charge", "Seismic Charge", "seismic-charge-face", "Seismic Charge", "7f8871", 0.375),
        Bomb("proton-bomb", "Proton Bomb", "proton-bomb-face", "Proton Bomb", "0a7578", 0.375),
        Bomb("ion-bomb", "Ion Bomb", "ion-bomb-face", "Ion Bomb", "b9dfc1", 0.375),
        Bomb("thermal-detonator", "Thermal Detonator", "thermal-detonator-face", "Thermal Detonator", "8bff13", 0.375),
        Bomb("bomblet", "Bomblet", "bomblet-face", "Bomblet", "982496", 0.375),
        new("proximity-mine", "Proximity Mine", "Custom_Model", "proximity-mine-mesh", "proximity-mine-face", "proximity-mine-collider", "Proximity Mine", "c8d044", 0.375, 1),
        new("cluster-mine", "Cluster Mine", "Custom_Token", null, "cluster-mine-centre-face", null, "Cluster Mine (middle)", "72ddce", 0.455393642, 3),
        new("conner-net", "Conner Net", "Custom_Token", null, "conner-net-face", null, "Connor Net", "7b2fc1", 0.6428425, 1),
        new("rigged-cargo", "Rigged Cargo", "Custom_Token", null, "rigged-cargo-face", null, "Loose Cargo", "7a6184", 0.8062451, 1)
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R21 First Edition Device Token Import");
        Console.WriteLine("================================================================");
        Console.WriteLine();
        if (args.Length < 2) { ShowUsage(); return 1; }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "Approved Unified 2.5 reference save");

            ValidateReferenceSave(referenceSave);
            var destinationRoot = Resolve(repository, "assets/source/unified1e/gameplay-tokens/devices");
            var manifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/device-gameplay-tokens.json");
            var reportPath = Resolve(repository, "_unifiedtoolkit_reports/phase16/device-token-import/first-edition-device-token-import.json");
            Directory.CreateDirectory(destinationRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

            var imported = new List<ImportedAsset>();
            foreach (var seed in Assets)
            {
                var source = Resolve(repository, Unified25Root + seed.SourceRelativePath);
                var destination = Path.Combine(destinationRoot, seed.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
                RequireFile(source, $"Unified 2.5 device source '{seed.Id}'");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var resolution = File.Exists(destination) && Hash(destination) == Hash(source)
                    ? "already-canonical"
                    : "repository-reuse";
                if (resolution == "repository-reuse") File.Copy(source, destination, true);
                if (Hash(source) != Hash(destination)) throw new InvalidDataException($"Copied device asset does not match source: {seed.Id}");
                imported.Add(Inspect(repository, seed, source, destination, resolution));
            }

            var assetIndex = imported.ToDictionary(asset => asset.Id, StringComparer.OrdinalIgnoreCase);
            var tokens = Devices.Select(device => Token(repository, device, assetIndex)).ToList();
            var manifest = new DeviceManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Phase = "16E-R21",
                Policy = "Visually approved Unified 2.5 device assets copied byte-for-byte into canonical First Edition paths. No runtime Lua or gameplay registry was copied or modified.",
                SourceSavePath = Normalise(referenceSave),
                SourceSaveSha256 = Hash(referenceSave),
                AssetCount = imported.Count,
                TokenCount = tokens.Count,
                PhysicalObjectCount = tokens.Sum(token => token.PhysicalPieceCount),
                Assets = imported,
                Tokens = tokens,
                ClusterMineConstruction = ClusterRecipe(repository, assetIndex)
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteReport(reportPath, repository, referenceSave, manifestPath, manifest);

            var refresh = !args.Any(value => value.Equals("--no-knowledge-base", StringComparison.OrdinalIgnoreCase));
            if (refresh)
            {
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var result = BuildKnowledgeBaseCommand.Run([repository]);
                if (result != 0) throw new InvalidOperationException($"Knowledge-base refresh returned exit code {result}.");
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:                 {repository}");
            Console.WriteLine($"Reference save:             {referenceSave}");
            Console.WriteLine($"Canonical device assets:    {manifest.AssetCount}");
            Console.WriteLine($"Canonical device types:     {manifest.TokenCount}");
            Console.WriteLine($"Physical device objects:    {manifest.PhysicalObjectCount}");
            Console.WriteLine($"Reused repository sources:  {imported.Count(asset => asset.Resolution == "repository-reuse")}");
            Console.WriteLine($"Already canonical:          {imported.Count(asset => asset.Resolution == "already-canonical")}");
            Console.WriteLine("Images modified:            0");
            Console.WriteLine("Lua scripts copied/modified:0");
            Console.WriteLine();
            Console.WriteLine($"Destination: {destinationRoot}");
            Console.WriteLine($"Manifest:    {manifestPath}");
            Console.WriteLine($"Report:      {reportPath}");
            Console.WriteLine($"Knowledge base refresh: {(refresh ? "Yes" : "No")}");
            Console.WriteLine();
            Console.WriteLine("First Edition device tokens imported successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition device token import failed: {exception.Message}");
            return 1;
        }
    }

    private static DeviceSeed Bomb(string id, string name, string face, string sourceName, string guid, double scale) =>
        new(id, name, "Custom_Model", "device-bomb-mesh", face, "device-bomb-collider", sourceName, guid, scale, 1);

    private static void ValidateReferenceSave(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("ObjectStates", out var objects) || objects.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Reference save does not contain ObjectStates.");
        foreach (var seed in Devices)
        {
            var matches = objects.EnumerateArray().Where(item =>
                Text(item, "Nickname").Equals(seed.SourceNickname, StringComparison.OrdinalIgnoreCase) &&
                Text(item, "GUID").Equals(seed.SourceGuid, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1)
                throw new InvalidDataException($"Expected exactly one approved source object '{seed.SourceNickname}' ({seed.SourceGuid}); found {matches.Count}.");
        }
    }

    private static ImportedAsset Inspect(string repository, AssetSeed seed, string source, string destination, string resolution)
    {
        var asset = new ImportedAsset
        {
            Id = seed.Id,
            Role = seed.Role,
            RepositoryPath = Relative(repository, destination),
            SourceRepositoryPath = Relative(repository, source),
            OriginalSourceUrl = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/" + seed.SourceRelativePath,
            SizeBytes = new FileInfo(destination).Length,
            Sha256 = Hash(destination),
            Resolution = resolution
        };
        if (seed.Role == "texture")
        {
            using var bitmap = SKBitmap.Decode(destination) ?? throw new InvalidDataException($"Could not decode texture: {destination}");
            asset.Width = bitmap.Width;
            asset.Height = bitmap.Height;
            asset.HasAlpha = bitmap.AlphaType != SKAlphaType.Opaque;
        }
        else
        {
            asset.Mesh = InspectObj(destination);
        }
        return asset;
    }

    private static DeviceToken Token(string repository, DeviceSeed seed, IReadOnlyDictionary<string, ImportedAsset> assets)
    {
        var face = assets[seed.FaceAssetId];
        return new DeviceToken
        {
            Id = seed.Id,
            Name = seed.Name,
            ObjectType = seed.ObjectType,
            MeshAssetId = seed.MeshAssetId,
            FaceAssetId = seed.FaceAssetId,
            ColliderAssetId = seed.ColliderAssetId,
            MeshPath = seed.MeshAssetId is null ? string.Empty : assets[seed.MeshAssetId].RepositoryPath,
            FacePath = face.RepositoryPath,
            ColliderPath = seed.ColliderAssetId is null ? string.Empty : assets[seed.ColliderAssetId].RepositoryPath,
            Scale = seed.Scale,
            PhysicalPieceCount = seed.PhysicalPieceCount,
            SourceObjectGuid = seed.SourceGuid,
            SourceObjectNickname = seed.SourceNickname,
            RuntimeStatus = "canonical-assets-runtime-deferred",
            LuaIncluded = false,
            AdditionalFacePaths = seed.Id == "cluster-mine"
                ? [assets["cluster-mine-side-face"].RepositoryPath]
                : []
        };
    }

    private static ClusterMineRecipe ClusterRecipe(string repository, IReadOnlyDictionary<string, ImportedAsset> assets) => new()
    {
        Status = "canonical-construction-recipe-runtime-deferred",
        CentreObjectType = "Custom_Token",
        CentreFacePath = assets["cluster-mine-centre-face"].RepositoryPath,
        CentreScale = 0.455393642,
        CentreDeploymentVerticalOffsetMillimetres = -2.0,
        SideObjectType = "Custom_Token",
        SideFacePath = assets["cluster-mine-side-face"].RepositoryPath,
        SideCount = 2,
        SideScale = 0.4554,
        SideOffsetMillimetres = new Coordinate { X = 43.5, Y = 0.0, Z = -1.5 },
        CustomTokenThickness = 0.1,
        MergeDistancePixels = 5.0,
        LuaIncluded = false,
        RuntimeImplementationDeferred = true
    };

    private static MeshInfo InspectObj(string path)
    {
        var vertices = new List<(double X, double Y, double Z)>();
        var uv = 0; var faces = 0;
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
            vertices.Add((x, y, z));
        }
        if (vertices.Count == 0) throw new InvalidDataException($"OBJ contains no vertices: {path}");
        return new MeshInfo
        {
            VertexCount = vertices.Count, UvCount = uv, FaceCount = faces,
            Width = vertices.Max(v => v.X) - vertices.Min(v => v.X),
            Height = vertices.Max(v => v.Y) - vertices.Min(v => v.Y),
            Depth = vertices.Max(v => v.Z) - vertices.Min(v => v.Z)
        };
    }

    private static void WriteReport(string path, string repository, string referenceSave, string manifestPath, DeviceManifest manifest)
    {
        var report = new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["GeneratedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["Phase"] = "16E-R21",
            ["Decision"] = "All nine reviewed First Edition device definitions approved",
            ["ReferenceSavePath"] = Normalise(referenceSave),
            ["ReferenceSaveSha256"] = Hash(referenceSave),
            ["ManifestPath"] = Relative(repository, manifestPath),
            ["CanonicalAssetCount"] = manifest.AssetCount,
            ["CanonicalDeviceTypeCount"] = manifest.TokenCount,
            ["PhysicalObjectCount"] = manifest.PhysicalObjectCount,
            ["ClusterMinePhysicalPieceCount"] = 3,
            ["FilesCopiedByteForByte"] = true,
            ["ImagesModified"] = 0,
            ["LuaScriptsCopiedOrModified"] = 0,
            ["RuntimeGameplayEnabled"] = false
        };
        File.WriteAllText(path, report.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static string Normalise(string path) => path.Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found: {path}", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: import-first-edition-device-tokens <first-edition-repo-folder> <tts-reference-save.json> [--no-knowledge-base]");

    private sealed record AssetSeed(string Id, string Role, string SourceRelativePath, string DestinationRelativePath);
    private sealed record DeviceSeed(string Id, string Name, string ObjectType, string? MeshAssetId, string FaceAssetId,
        string? ColliderAssetId, string SourceNickname, string SourceGuid, double Scale, int PhysicalPieceCount);

    public sealed class DeviceManifest
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
        public string SourceSavePath { get; set; } = string.Empty;
        public string SourceSaveSha256 { get; set; } = string.Empty;
        public int AssetCount { get; set; }
        public int TokenCount { get; set; }
        public int PhysicalObjectCount { get; set; }
        public List<ImportedAsset> Assets { get; set; } = [];
        public List<DeviceToken> Tokens { get; set; } = [];
        public ClusterMineRecipe ClusterMineConstruction { get; set; } = new();
    }

    public sealed class ImportedAsset
    {
        public string Id { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = string.Empty;
        public string SourceRepositoryPath { get; set; } = string.Empty;
        public string OriginalSourceUrl { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool? HasAlpha { get; set; }
        public MeshInfo? Mesh { get; set; }
    }

    public sealed class DeviceToken
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string? MeshAssetId { get; set; }
        public string FaceAssetId { get; set; } = string.Empty;
        public string? ColliderAssetId { get; set; }
        public string MeshPath { get; set; } = string.Empty;
        public string FacePath { get; set; } = string.Empty;
        public string ColliderPath { get; set; } = string.Empty;
        public List<string> AdditionalFacePaths { get; set; } = [];
        public double Scale { get; set; }
        public int PhysicalPieceCount { get; set; }
        public string SourceObjectGuid { get; set; } = string.Empty;
        public string SourceObjectNickname { get; set; } = string.Empty;
        public string RuntimeStatus { get; set; } = string.Empty;
        public bool LuaIncluded { get; set; }
    }

    public sealed class ClusterMineRecipe
    {
        public string Status { get; set; } = string.Empty;
        public string CentreObjectType { get; set; } = string.Empty;
        public string CentreFacePath { get; set; } = string.Empty;
        public double CentreScale { get; set; }
        public double CentreDeploymentVerticalOffsetMillimetres { get; set; }
        public string SideObjectType { get; set; } = string.Empty;
        public string SideFacePath { get; set; } = string.Empty;
        public int SideCount { get; set; }
        public double SideScale { get; set; }
        public Coordinate SideOffsetMillimetres { get; set; } = new();
        public double CustomTokenThickness { get; set; }
        public double MergeDistancePixels { get; set; }
        public bool LuaIncluded { get; set; }
        public bool RuntimeImplementationDeferred { get; set; }
    }

    public sealed class Coordinate { public double X { get; set; } public double Y { get; set; } public double Z { get; set; } }
    public sealed class MeshInfo
    {
        public int VertexCount { get; set; }
        public int UvCount { get; set; }
        public int FaceCount { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }
    }
}
