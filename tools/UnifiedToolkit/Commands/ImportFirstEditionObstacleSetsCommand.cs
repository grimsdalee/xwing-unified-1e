using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Promotes the 18 visually approved standard obstacle objects into canonical
/// First Edition paths. Pride states, attached distance lines and Lua are not
/// copied. Higher-resolution artwork and UV remapping remain deferred.
/// </summary>
public static class ImportFirstEditionObstacleSetsCommand
{
    private const string Unified25Root = "assets/source/unified25/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly ObstacleSeed[] Obstacles = BuildObstacles();
    private static readonly AssetSeed[] Assets = BuildAssets();

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R23 First Edition Obstacle Set Import");
        Console.WriteLine("=================================================================");
        Console.WriteLine();
        if (args.Length < 2) { ShowUsage(); return 1; }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "Approved Unified 2.5 reference save");
            ValidateReferenceSave(referenceSave);

            var destinationRoot = Resolve(repository, "assets/source/unified1e/gameplay-tokens/obstacles");
            var manifestPath = Resolve(repository, "assets/source/unified1e/reference/gameplay-objects/obstacle-gameplay-tokens.json");
            var reportPath = Resolve(repository, "_unifiedtoolkit_reports/phase16/obstacle-set-import/first-edition-obstacle-set-import.json");
            Directory.CreateDirectory(destinationRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

            var imported = new List<ImportedObstacleAsset>();
            foreach (var seed in Assets)
            {
                var source = Resolve(repository, Unified25Root + seed.SourceRelativePath);
                var destination = Path.Combine(destinationRoot, seed.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
                RequireFile(source, $"Unified 2.5 obstacle source '{seed.Id}'");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var resolution = File.Exists(destination) && Hash(destination) == Hash(source)
                    ? "already-canonical" : "repository-reuse";
                if (resolution == "repository-reuse") File.Copy(source, destination, true);
                if (Hash(source) != Hash(destination)) throw new InvalidDataException($"Copied obstacle asset does not match source: {seed.Id}");
                imported.Add(Inspect(repository, seed, source, destination, resolution));
            }

            var assets = imported.ToDictionary(asset => asset.Id, StringComparer.OrdinalIgnoreCase);
            var tokens = Obstacles.Select(obstacle => new CanonicalObstacle
            {
                Id = obstacle.Id,
                Name = obstacle.Name,
                SetId = obstacle.SetId,
                PieceNumber = obstacle.PieceNumber,
                ObjectType = "Custom_Model",
                MeshAssetId = obstacle.MeshAssetId,
                FaceAssetId = obstacle.FaceAssetId,
                ColliderAssetId = obstacle.ColliderAssetId,
                MeshPath = assets[obstacle.MeshAssetId].RepositoryPath,
                FacePath = assets[obstacle.FaceAssetId].RepositoryPath,
                ColliderPath = assets[obstacle.ColliderAssetId].RepositoryPath,
                Scale = obstacle.Scale,
                SourceObjectGuid = obstacle.SourceGuid,
                SourceObjectNickname = obstacle.SourceNickname,
                RuntimeStatus = "canonical-assets-runtime-deferred",
                AlternateStatesIncluded = false,
                AttachedVectorLinesIncluded = false,
                LuaIncluded = false
            }).ToList();

            var manifest = new ObstacleManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Phase = "16E-R23",
                Policy = "Approved standard obstacle assets copied byte-for-byte. Pride states, attached vector distance lines and Lua are excluded. High-resolution artwork replacement and UV remapping are deferred.",
                SourceSavePath = Normalise(referenceSave),
                SourceSaveSha256 = Hash(referenceSave),
                AssetCount = imported.Count,
                SetCount = 3,
                TokenCount = tokens.Count,
                Assets = imported,
                Tokens = tokens,
                Sets =
                [
                    Set("core-asteroids", "Core Set Asteroids", "core-asteroid", "core-asteroids-face", tokens),
                    Set("tfa-asteroids", "The Force Awakens Asteroids", "tfa-asteroid", "tfa-asteroids-face", tokens),
                    Set("debris-clouds", "Debris Clouds", "debris-cloud", "debris-clouds-face", tokens)
                ],
                ArtworkUpgrade = new ArtworkUpgradePlan
                {
                    Status = "deferred-approved-current-artwork",
                    PreferredCandidateDirectory = "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/asteroid-tokens",
                    Requirement = "Preserve exact physical silhouettes and remap all 18 obstacle UV layouts before replacing the three current atlases.",
                    CurrentAtlasCount = 3,
                    IndividualObstacleCount = 18
                }
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

            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Reference save:                {referenceSave}");
            Console.WriteLine($"Canonical obstacle assets:     {manifest.AssetCount}");
            Console.WriteLine($"Canonical obstacle sets:       {manifest.SetCount}");
            Console.WriteLine($"Canonical physical obstacles:  {manifest.TokenCount}");
            Console.WriteLine($"Reused repository sources:     {imported.Count(asset => asset.Resolution == "repository-reuse")}");
            Console.WriteLine($"Already canonical:             {imported.Count(asset => asset.Resolution == "already-canonical")}");
            Console.WriteLine("Alternate states imported:     0");
            Console.WriteLine("Attached distance lines copied:0");
            Console.WriteLine("Images modified:               0");
            Console.WriteLine("Lua scripts copied/modified:   0");
            Console.WriteLine();
            Console.WriteLine($"Destination: {destinationRoot}");
            Console.WriteLine($"Manifest:    {manifestPath}");
            Console.WriteLine($"Report:      {reportPath}");
            Console.WriteLine($"Knowledge base refresh: {(refresh ? "Yes" : "No")}");
            Console.WriteLine();
            Console.WriteLine("First Edition obstacle sets imported successfully. Files were copied byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition obstacle set import failed: {exception.Message}");
            return 1;
        }
    }

    private static ObstacleSet Set(string id, string name, string prefix, string faceAssetId, List<CanonicalObstacle> tokens) => new()
    {
        Id = id,
        Name = name,
        PieceCount = 6,
        FaceAssetId = faceAssetId,
        TokenIds = tokens.Where(token => token.SetId == id).OrderBy(token => token.PieceNumber).Select(token => token.Id).ToList()
    };

    private static void ValidateReferenceSave(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var objects = document.RootElement.GetProperty("ObjectStates").EnumerateArray().ToList();
        foreach (var obstacle in Obstacles)
        {
            var matches = objects.Where(item => Text(item, "GUID") == obstacle.SourceGuid &&
                Text(item, "Nickname") == obstacle.SourceNickname).ToList();
            if (matches.Count != 1) throw new InvalidDataException($"Expected one approved source object '{obstacle.SourceNickname}' ({obstacle.SourceGuid}); found {matches.Count}.");
        }
    }

    private static ImportedObstacleAsset Inspect(string repository, AssetSeed seed, string source, string destination, string resolution)
    {
        var result = new ImportedObstacleAsset
        {
            Id = seed.Id,
            Role = seed.Role,
            RepositoryPath = Relative(repository, destination),
            SourceRepositoryPath = Relative(repository, source),
            OriginalSourceUrl = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/" + EscapeUrlPath(seed.SourceRelativePath),
            SizeBytes = new FileInfo(destination).Length,
            Sha256 = Hash(destination),
            Resolution = resolution
        };
        if (seed.Role == "texture")
        {
            using var image = SKBitmap.Decode(destination) ?? throw new InvalidDataException($"Could not decode obstacle texture: {destination}");
            result.Width = image.Width;
            result.Height = image.Height;
            result.HasAlpha = image.AlphaType != SKAlphaType.Opaque;
        }
        else result.Mesh = InspectObj(destination);
        return result;
    }

    private static ObstacleSeed[] BuildObstacles()
    {
        var list = new List<ObstacleSeed>();
        var coreGuids = new[] { "9564c7", "ed9fcc", "6925de", "7fa7b7", "bc156b", "1f74b0" };
        var tfaGuids = new[] { "ac1f52", "4e1f1e", "54eca6", "e22584", "62c2e4", "157bbd" };
        var debrisGuids = new[] { "f2766c", "fcf984", "f43a6a", "72ac5e", "46114f", "416398" };
        for (var index = 1; index <= 6; index++)
        {
            list.Add(new($"core-asteroid-{index}", $"Core Asteroid {index}", "core-asteroids", index,
                $"core-asteroid-{index}-mesh", "core-asteroids-face", $"core-asteroid-{index}-collider",
                $"Asteroid {index}", coreGuids[index - 1], 1.0));
            list.Add(new($"tfa-asteroid-{index}", $"TFA Asteroid {index}", "tfa-asteroids", index,
                $"tfa-asteroid-{index}-mesh", "tfa-asteroids-face", $"tfa-asteroid-{index}-collider",
                $"TFA Asteroid {index}", tfaGuids[index - 1], 1.0));
            list.Add(new($"debris-cloud-{index}", $"Debris Cloud {index}", "debris-clouds", index,
                $"debris-cloud-{index}-mesh", "debris-clouds-face", $"debris-cloud-{index}-collider",
                $"Debrisfield {index}", debrisGuids[index - 1], index == 1 ? 1.0999999 : 1.1));
        }
        return list.ToArray();
    }

    private static AssetSeed[] BuildAssets()
    {
        var list = new List<AssetSeed>
        {
            new("core-asteroids-face", "texture", "assets/textures/obstacles/Core Astroids All.png", "faces/core-asteroids.png"),
            new("tfa-asteroids-face", "texture", "assets/textures/obstacles/TFA Astroids All.png", "faces/tfa-asteroids.png"),
            new("debris-clouds-face", "texture", "assets/textures/obstacles/Debrisfield All.png", "faces/debris-clouds.png")
        };
        for (var index = 1; index <= 6; index++)
        {
            list.Add(new($"core-asteroid-{index}-mesh", "mesh", $"assets/models/obstacles/Core{index}-model.obj", $"meshes/core-asteroid-{index}.obj"));
            list.Add(new($"core-asteroid-{index}-collider", "collider", $"assets/models/obstacles/Core{index}-col.obj", $"colliders/core-asteroid-{index}.obj"));
            list.Add(new($"tfa-asteroid-{index}-mesh", "mesh", $"assets/models/obstacles/TFA{index}-model.obj", $"meshes/tfa-asteroid-{index}.obj"));
            list.Add(new($"tfa-asteroid-{index}-collider", "collider", $"assets/models/obstacles/TFA{index}-col.obj", $"colliders/tfa-asteroid-{index}.obj"));
            list.Add(new($"debris-cloud-{index}-mesh", "mesh", $"assets/models/obstacles/Debris{index}-model.obj", $"meshes/debris-cloud-{index}.obj"));
            list.Add(new($"debris-cloud-{index}-collider", "collider", $"assets/models/obstacles/Debris{index}-col.obj", $"colliders/debris-cloud-{index}.obj"));
        }
        return list.ToArray();
    }

    private static MeshInfo InspectObj(string path)
    {
        var vertices = new List<(double X, double Y, double Z)>(); var uv = 0; var faces = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("vt ")) { uv++; continue; }
            if (line.StartsWith("f ")) { faces++; continue; }
            if (!line.StartsWith("v ")) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
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

    private static void WriteReport(string path, string repository, string save, string manifest, ObstacleManifest data)
    {
        var report = new
        {
            SchemaVersion = 1,
            GeneratedUtc = DateTimeOffset.UtcNow,
            Phase = "16E-R23",
            Decision = "All three required First Edition obstacle sets approved",
            ReferenceSavePath = Normalise(save),
            ReferenceSaveSha256 = Hash(save),
            ManifestPath = Relative(repository, manifest),
            data.AssetCount,
            data.SetCount,
            data.TokenCount,
            AlternateStatesImported = 0,
            AttachedVectorLinesCopied = 0,
            ImagesModified = 0,
            LuaScriptsCopiedOrModified = 0,
            RuntimeGameplayEnabled = false,
            data.ArtworkUpgrade
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
    }

    private static string EscapeUrlPath(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));
    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Resolve(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static string Normalise(string path) => path.Replace('\\', '/');
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found: {path}", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: import-first-edition-obstacle-sets <first-edition-repo-folder> <tts-reference-save.json> [--no-knowledge-base]");

    private sealed record AssetSeed(string Id, string Role, string SourceRelativePath, string DestinationRelativePath);
    private sealed record ObstacleSeed(string Id, string Name, string SetId, int PieceNumber, string MeshAssetId, string FaceAssetId,
        string ColliderAssetId, string SourceNickname, string SourceGuid, double Scale);

    public sealed class ObstacleManifest
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
        public string SourceSavePath { get; set; } = string.Empty;
        public string SourceSaveSha256 { get; set; } = string.Empty;
        public int AssetCount { get; set; }
        public int SetCount { get; set; }
        public int TokenCount { get; set; }
        public List<ImportedObstacleAsset> Assets { get; set; } = [];
        public List<CanonicalObstacle> Tokens { get; set; } = [];
        public List<ObstacleSet> Sets { get; set; } = [];
        public ArtworkUpgradePlan ArtworkUpgrade { get; set; } = new();
    }
    public sealed class ImportedObstacleAsset
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
    public sealed class CanonicalObstacle
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SetId { get; set; } = string.Empty;
        public int PieceNumber { get; set; }
        public string ObjectType { get; set; } = string.Empty;
        public string MeshAssetId { get; set; } = string.Empty;
        public string FaceAssetId { get; set; } = string.Empty;
        public string ColliderAssetId { get; set; } = string.Empty;
        public string MeshPath { get; set; } = string.Empty;
        public string FacePath { get; set; } = string.Empty;
        public string ColliderPath { get; set; } = string.Empty;
        public double Scale { get; set; }
        public string SourceObjectGuid { get; set; } = string.Empty;
        public string SourceObjectNickname { get; set; } = string.Empty;
        public string RuntimeStatus { get; set; } = string.Empty;
        public bool AlternateStatesIncluded { get; set; }
        public bool AttachedVectorLinesIncluded { get; set; }
        public bool LuaIncluded { get; set; }
    }
    public sealed class ObstacleSet
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PieceCount { get; set; }
        public string FaceAssetId { get; set; } = string.Empty;
        public List<string> TokenIds { get; set; } = [];
    }
    public sealed class ArtworkUpgradePlan
    {
        public string Status { get; set; } = string.Empty;
        public string PreferredCandidateDirectory { get; set; } = string.Empty;
        public string Requirement { get; set; } = string.Empty;
        public int CurrentAtlasCount { get; set; }
        public int IndividualObstacleCount { get; set; }
    }
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
