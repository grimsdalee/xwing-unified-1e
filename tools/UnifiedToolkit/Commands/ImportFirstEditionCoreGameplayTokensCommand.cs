using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R8 promotes the approved original First Edition core token assets
/// into semantic canonical paths. Existing byte-identical files are reused by
/// SHA-256; missing originals may be downloaded from their save-embedded URL.
/// No runtime Lua or gameplay registry is changed.
/// </summary>
public static class ImportFirstEditionCoreGameplayTokensCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly AssetSeed[] Assets =
    [
        new("shield-mesh", "shield", "mesh", "meshes/shield.obj", 26737, "6849ce9a527a1becbff4ad65274b394b275d40e2df70ffe8b8e93c0e395ac484", "https://steamusercontent-a.akamaihd.net/ugc/2496767915170502145/42D16D55AF7683BDAB0537F1E223D0F6C3491326/", "assets/source/legacy1e/steamusercontent-a.akamaihd.net/other/asset__8444433b4d8321f1.obj"),
        new("shield-face", "shield", "texture", "faces/shield.png", 252204, "8c8364f3d6c048a303b99aba2c4c2534550607bcc09bf93a8b35d7dae0718b2c", "https://steamusercontent-a.akamaihd.net/ugc/2496767915170504458/76C5CC7F64A5EFC23BA6F62433401590374F2356/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__3744543d9a02636d.png"),
        new("critical-hit-mesh", "critical-hit", "mesh", "meshes/critical-hit.obj", 14739, "2bd547b6db7a19839ed9c114226a5d5e8c6c3388d9ec778f4e79f0c22ebd400e", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186014915/FF751A206976EBD2B75A29196FDCBB590ECB0A43/", null),
        new("critical-hit-face", "critical-hit", "texture", "faces/critical-hit.png", 118896, "d974a884a74daf11400b91ad10e98089d342b26212b82c774ff2d4ee94da0274", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015037/8E267724200C90DB40DE6199A329409F0053C2EE/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__52d1e774a84eb48e.png"),
        new("round-focus-weapons-disabled-mesh", "focus,weapons-disabled", "mesh", "meshes/round-focus-weapons-disabled.obj", 52108, "e5f9594c6c4db7134c8741c05fc71ef88a786c532392152753c4d193c1956565", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015319/AD083B62803F2B490CF71229427AF96B396BBE56/", "assets/source/legacy1e/steamusercontent-a.akamaihd.net/other/asset__cc9e59fdab33492b.obj"),
        new("weapons-disabled-face", "weapons-disabled", "texture", "faces/weapons-disabled.png", 112181, "7a163a414bb7872bc0bfb5b1860623b3e147fc9b6418e18a3473925e998bbc18", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015436/07006646E4EA210F01282FAEF0FC3E719CD3B570/", null),
        new("focus-face", "focus", "texture", "faces/focus.png", 149927, "c3804e86de6188545fc30e1e988c9f0bb6ef7c38147e931d30c5d5f34e9269a3", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015534/D5CE2813844527DB2C7F4DFA52C15668BFFEE98E/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__c0369b29f1d67ad4.png"),
        new("evade-mesh", "evade", "mesh", "meshes/evade.obj", 52070, "e746cc3cbb92cf11b2a78024573f6e2a48293f50de27b16f706ccbd075d21b9e", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015638/51789A70E1CC656ED88526C872E6E44C8121D445/", "assets/source/legacy1e/steamusercontent-a.akamaihd.net/other/asset__66a2c7806bf2d5ec.obj"),
        new("evade-face", "evade", "texture", "faces/evade.png", 133832, "ee8270c1c5bd063c00dfdd19401db633f927d7c39001bdcf52fd0f7cce0447e8", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015738/A31BCA6B5E3ED6F8031F1DDD667A37DBB2C7AEB2/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__4264989d2c8db9c5.png"),
        new("stress-mesh", "stress", "mesh", "meshes/stress.obj", 24016, "a11f7fcb192cab869fcf88431943337b395229eb86979e89b43feb217421549b", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015857/1C403FD63E98A5D96895CDBD0CDBC577C1091121/", "assets/source/legacy1e/steamusercontent-a.akamaihd.net/other/asset__7f997ffbf00b08ec.obj"),
        new("stress-face", "stress", "texture", "faces/stress.png", 140111, "da45a907210e60e0af5e1729a89f405d7fefa3befe641625bbf257abaadbe06c", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186015957/57E31A59B8A426555BB0703F6B402CA0B4234EC1/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__fa8ed658e21b949d.png"),
        new("ion-mesh", "ion", "mesh", "meshes/ion.obj", 20255, "2247b1be2fea68414d40b6268ba7faeb9476b2f71788e435954f8dfb527f68ab", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186016043/C64727A9ACE867EFB8B5743BB8AFCDF82CFE0F23/", null),
        new("ion-face", "ion", "texture", "faces/ion.png", 1541760, "f6301996934292fb4710589a902dca5c76f3de4ddb2cccc37e7f0cf61dc992e7", "https://steamusercontent-a.akamaihd.net/ugc/2496767915186016181/0FC0888A3D6F570886AD43808F794F890EAFDAED/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__6b7be0188e4ba31f.png"),
        new("reinforce-mesh", "reinforce", "mesh", "meshes/reinforce.obj", 17640, "53350015d65d829a2bc49083a9f3f41b35e3927263854079dc264b3ff23d4ea0", "https://steamusercontent-a.akamaihd.net/ugc/830199836523210430/725644DA3EB593D6C7FF7AE77040CE8F07495C4D/", "assets/source/legacy1e/steamusercontent-a.akamaihd.net/other/asset__57c9e087a6f67b6e.obj"),
        new("reinforce-face", "reinforce", "texture", "faces/reinforce.png", 456862, "33a5efc5f0db73a8cbd8df735a1151eb0149afd8730e649a18330b878eeca414", "https://steamusercontent-a.akamaihd.net/ugc/830199836523210833/D6A115DE169F12E35F6ECBF222289819FC958B94/", "assets/source/legacy1e-non-pilot/steamusercontent-a.akamaihd.net/general-tokens/asset__cfe55b8b691cf228.png")
    ];

    private static readonly TokenSeed[] Tokens =
    [
        new("shield", "Shield", "shield-mesh", "shield-face"),
        new("critical-hit", "Critical Hit", "critical-hit-mesh", "critical-hit-face"),
        new("weapons-disabled", "Weapon Disabled", "round-focus-weapons-disabled-mesh", "weapons-disabled-face"),
        new("focus", "Focus", "round-focus-weapons-disabled-mesh", "focus-face"),
        new("evade", "Evade", "evade-mesh", "evade-face"),
        new("stress", "Stress", "stress-mesh", "stress-face"),
        new("ion", "Ion", "ion-mesh", "ion-face"),
        new("reinforce", "Reinforce (aft)", "reinforce-mesh", "reinforce-face")
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R8 Core Gameplay Token Import");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "core-gameplay-token-import"));
            var assetBaseUrl = (Option(args, "--asset-base-url") ??
                "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/").TrimEnd('/') + "/";
            var allowDownload = !args.Any(value => value.Equals("--no-download", StringComparison.OrdinalIgnoreCase));
            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "X-Wing 1.0 reference save");

            var destinationRoot = Path.Combine(repository, "assets", "source", "unified1e", "gameplay-tokens");
            var manifestPath = Path.Combine(repository, "assets", "source", "unified1e", "reference", "gameplay-objects", "core-gameplay-tokens.json");
            Directory.CreateDirectory(destinationRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            Directory.CreateDirectory(output);

            var importedAssets = new List<ImportedCoreTokenAsset>();
            var scanCache = new Dictionary<(long Size, string Hash), string>();
            foreach (var seed in Assets)
            {
                var destination = Path.Combine(destinationRoot, seed.RelativeDestination.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var resolution = ResolveSource(repository, destination, seed, scanCache, allowDownload);
                if (!resolution.Success)
                    throw new InvalidOperationException($"{seed.Id}: {resolution.Message}");
                importedAssets.Add(InspectImportedAsset(repository, seed, destination, resolution));
            }

            var assetIndex = importedAssets.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var tokenDefinitions = Tokens.Select(token => new CoreGameplayTokenDefinition
            {
                Id = token.Id,
                Name = token.Nickname,
                MeshAssetId = token.MeshAssetId,
                FaceAssetId = token.FaceAssetId,
                MeshPath = assetIndex[token.MeshAssetId].RepositoryPath,
                FacePath = assetIndex[token.FaceAssetId].RepositoryPath,
                RuntimeStatus = "asset-validation-only",
                LuaIncluded = false
            }).ToList();
            var manifest = new CoreGameplayTokenManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Original First Edition assets promoted byte-for-byte. Runtime Lua and gameplay registries are unchanged.",
                SourceSavePath = NormalisePath(referenceSave),
                SourceSaveSha256 = HashFile(referenceSave),
                AssetCount = importedAssets.Count,
                TokenCount = tokenDefinitions.Count,
                Assets = importedAssets,
                Tokens = tokenDefinitions
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            var savePath = Path.Combine(output, "first-edition-core-gameplay-token-validation.json");
            var reportPath = Path.Combine(output, "FIRST-EDITION-CORE-GAMEPLAY-TOKEN-IMPORT.md");
            var csvPath = Path.Combine(output, "first-edition-core-gameplay-token-assets.csv");
            File.WriteAllText(savePath, BuildValidationSave(referenceSave, tokenDefinitions, assetBaseUrl).ToJsonString(JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, importedAssets);
            WriteReport(reportPath, manifest, manifestPath, savePath);

            Console.WriteLine($"Repository:                 {repository}");
            Console.WriteLine($"Reference save:             {referenceSave}");
            Console.WriteLine($"Canonical token assets:     {importedAssets.Count}");
            Console.WriteLine($"Canonical token objects:    {tokenDefinitions.Count}");
            Console.WriteLine($"Reused existing sources:    {importedAssets.Count(item => item.Resolution == "repository-reuse")}");
            Console.WriteLine($"Downloaded originals:       {importedAssets.Count(item => item.Resolution == "downloaded")}");
            Console.WriteLine($"Already canonical:          {importedAssets.Count(item => item.Resolution == "already-canonical")}");
            Console.WriteLine($"Images modified:            0");
            Console.WriteLine($"Lua scripts added:          0");
            Console.WriteLine();
            Console.WriteLine($"Destination:     {destinationRoot}");
            Console.WriteLine($"Manifest:        {manifestPath}");
            Console.WriteLine($"Validation save: {savePath}");
            Console.WriteLine($"Report:          {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Core token assets imported successfully. Files were copied or downloaded byte-for-byte.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Core gameplay token import failed: {exception.Message}");
            return 1;
        }
    }

    private static SourceResolution ResolveSource(
        string repository,
        string destination,
        AssetSeed seed,
        Dictionary<(long Size, string Hash), string> scanCache,
        bool allowDownload)
    {
        if (File.Exists(destination) && Verify(destination, seed))
            return new SourceResolution(true, "already-canonical", destination, "Canonical file already matches.");

        if (!string.IsNullOrWhiteSpace(seed.PreferredRepositoryPath))
        {
            var preferred = RepositoryFile(repository, seed.PreferredRepositoryPath!);
            if (File.Exists(preferred) && Verify(preferred, seed))
            {
                File.Copy(preferred, destination, true);
                return new SourceResolution(true, "repository-reuse", preferred, "Reused preferred byte-identical repository source.");
            }
        }

        var key = (seed.SizeBytes, seed.Sha256);
        if (!scanCache.TryGetValue(key, out var match))
        {
            match = FindByHash(repository, destination, seed) ?? string.Empty;
            scanCache[key] = match;
        }
        if (!string.IsNullOrEmpty(match))
        {
            File.Copy(match, destination, true);
            return new SourceResolution(true, "repository-reuse", match, "Reused byte-identical repository source found by SHA-256.");
        }

        if (!allowDownload)
            return new SourceResolution(false, "unavailable", null, "No byte-identical repository source exists and downloads are disabled.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UnifiedToolkit/1.0 FirstEditionAssetImport");
        var bytes = client.GetByteArrayAsync(seed.SourceUrl).GetAwaiter().GetResult();
        if (bytes.LongLength != seed.SizeBytes || HashBytes(bytes) != seed.Sha256)
            return new SourceResolution(false, "download-verification-failed", null, "Downloaded bytes did not match the approved size and SHA-256.");
        File.WriteAllBytes(destination, bytes);
        return new SourceResolution(true, "downloaded", seed.SourceUrl, "Downloaded and verified original source bytes.");
    }

    private static string? FindByHash(string repository, string destination, AssetSeed seed)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(repository, "assets"), "*", SearchOption.AllDirectories))
        {
            if (path.Equals(destination, StringComparison.OrdinalIgnoreCase)) continue;
            var info = new FileInfo(path);
            if (info.Length != seed.SizeBytes) continue;
            if (HashFile(path) == seed.Sha256) return path;
        }
        return null;
    }

    private static ImportedCoreTokenAsset InspectImportedAsset(string repository, AssetSeed seed, string path, SourceResolution resolution)
    {
        if (!Verify(path, seed)) throw new InvalidDataException($"Imported asset failed verification: {path}");
        int? width = null;
        int? height = null;
        bool? hasAlpha = null;
        CoreTokenMeshBounds? bounds = null;
        if (seed.Role == "texture")
        {
            using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Could not decode imported texture: {path}");
            width = bitmap.Width;
            height = bitmap.Height;
            hasAlpha = bitmap.AlphaType != SKAlphaType.Opaque;
        }
        else
        {
            bounds = ReadBounds(path);
        }
        return new ImportedCoreTokenAsset
        {
            Id = seed.Id,
            TokenIds = seed.TokenIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Role = seed.Role,
            RepositoryPath = Relative(repository, path),
            SizeBytes = seed.SizeBytes,
            Sha256 = seed.Sha256,
            OriginalSourceUrl = seed.SourceUrl,
            Resolution = resolution.Resolution,
            ResolvedFrom = resolution.Source is null ? null : NormalisePath(resolution.Source),
            Width = width,
            Height = height,
            HasAlpha = hasAlpha,
            MeshBounds = bounds
        };
    }

    private static CoreTokenMeshBounds ReadBounds(string path)
    {
        var vertices = new List<(double X, double Y, double Z)>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("v ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                vertices.Add((x, y, z));
        }
        if (vertices.Count == 0) throw new InvalidDataException($"OBJ contains no readable vertices: {path}");
        var minX = vertices.Min(item => item.X); var maxX = vertices.Max(item => item.X);
        var minY = vertices.Min(item => item.Y); var maxY = vertices.Max(item => item.Y);
        var minZ = vertices.Min(item => item.Z); var maxZ = vertices.Max(item => item.Z);
        return new CoreTokenMeshBounds { VertexCount = vertices.Count, Width = maxX - minX, Height = maxY - minY, Depth = maxZ - minZ };
    }

    private static JsonObject BuildValidationSave(string referenceSave, List<CoreGameplayTokenDefinition> tokens, string assetBaseUrl)
    {
        using var source = JsonDocument.Parse(File.ReadAllBytes(referenceSave));
        var sourceObjects = FindSourceObjects(source.RootElement);
        var objects = new JsonArray();
        var startX = -((tokens.Count - 1) * 3.2f) / 2f;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!sourceObjects.TryGetValue(token.Name, out var sourceObject))
                throw new InvalidDataException($"Reference save does not contain a loose Custom_Model named '{token.Name}'.");
            var clone = JsonNode.Parse(sourceObject.GetRawText())?.AsObject()
                ?? throw new InvalidDataException($"Could not clone reference object '{token.Name}'.");
            clone["GUID"] = (index + 1).ToString("x6", CultureInfo.InvariantCulture);
            clone["Nickname"] = token.Name;
            clone["Description"] = $"Phase 16E-R8 canonical asset validation\nMesh: {token.MeshPath}\nFace: {token.FacePath}";
            clone["GMNotes"] = "No runtime Lua. Asset and physical-construction validation only.";
            clone["Transform"] = Transform(startX + index * 3.2f, 1.2f, 0f, SourceScale(sourceObject));
            clone["LuaScript"] = string.Empty;
            clone["LuaScriptState"] = string.Empty;
            clone["XmlUI"] = string.Empty;
            clone["Locked"] = false;
            var customMesh = clone["CustomMesh"]?.AsObject() ?? throw new InvalidDataException($"'{token.Name}' lacks CustomMesh.");
            customMesh["MeshURL"] = AssetUrl(assetBaseUrl, token.MeshPath);
            customMesh["DiffuseURL"] = AssetUrl(assetBaseUrl, token.FacePath);
            customMesh["NormalURL"] = string.Empty;
            customMesh["ColliderURL"] = string.Empty;
            objects.Add(clone);
        }
        var envelope = JsonNode.Parse(File.ReadAllText(referenceSave))?.AsObject()
            ?? throw new InvalidDataException("Could not parse the TTS reference-save envelope.");
        envelope["SaveName"] = "X-Wing Unified 1E - Phase 16E-R8 Core Token Asset Validation";
        envelope["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
        envelope["Note"] = "Validate original First Edition mesh, texture, scale, thickness, flipping and stacking. No gameplay Lua is included.";
        envelope["Rules"] = string.Empty;
        envelope["XmlUI"] = string.Empty;
        envelope["LuaScript"] = string.Empty;
        envelope["LuaScriptState"] = string.Empty;
        envelope["ObjectStates"] = objects;
        return envelope;
    }

    private static Dictionary<string, JsonElement> FindSourceObjects(JsonElement root)
    {
        if (!root.TryGetProperty("ObjectStates", out var states) || states.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Reference save does not contain ObjectStates.");
        var wanted = Tokens.Select(item => item.Nickname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in states.EnumerateArray())
        {
            if (String(item, "Name") != "Custom_Model") continue;
            var nickname = String(item, "Nickname");
            if (wanted.Contains(nickname) && !result.ContainsKey(nickname)) result[nickname] = item.Clone();
        }
        return result;
    }

    private static float SourceScale(JsonElement item)
    {
        if (item.TryGetProperty("Transform", out var transform) && transform.TryGetProperty("scaleX", out var scale) && scale.TryGetSingle(out var value)) return value;
        return 0.375f;
    }

    private static JsonObject Transform(float x, float y, float z, float scale) => new()
    {
        ["posX"] = x, ["posY"] = y, ["posZ"] = z,
        ["rotX"] = 0f, ["rotY"] = 180f, ["rotZ"] = 0f,
        ["scaleX"] = scale, ["scaleY"] = scale, ["scaleZ"] = scale
    };

    private static void WriteCsv(string path, IEnumerable<ImportedCoreTokenAsset> assets)
    {
        var lines = new List<string> { "Id,TokenIds,Role,RepositoryPath,SizeBytes,Sha256,Resolution,ResolvedFrom,Width,Height,HasAlpha,MeshWidth,MeshHeight,MeshDepth,VertexCount,OriginalSourceUrl" };
        foreach (var item in assets)
            lines.Add(Csv(item.Id, string.Join(';', item.TokenIds), item.Role, item.RepositoryPath, item.SizeBytes.ToString(CultureInfo.InvariantCulture), item.Sha256, item.Resolution, item.ResolvedFrom, item.Width?.ToString(), item.Height?.ToString(), item.HasAlpha?.ToString(), Format(item.MeshBounds?.Width), Format(item.MeshBounds?.Height), Format(item.MeshBounds?.Depth), item.MeshBounds?.VertexCount.ToString(), item.OriginalSourceUrl));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(string path, CoreGameplayTokenManifest manifest, string manifestPath, string savePath)
    {
        var lines = new List<string>
        {
            "# Phase 16E-R8 First Edition Core Gameplay Token Import", "",
            $"- Canonical assets: **{manifest.AssetCount}**",
            $"- Token objects: **{manifest.TokenCount}**",
            $"- Existing repository sources reused: **{manifest.Assets.Count(item => item.Resolution == "repository-reuse")}**",
            $"- Originals downloaded: **{manifest.Assets.Count(item => item.Resolution == "downloaded")}**",
            "- Images modified: **0**", "- Lua scripts added: **0**", "",
            "All canonical files were verified by expected byte length and SHA-256. Shared meshes are stored once.", "",
            $"- Canonical manifest: `{NormalisePath(manifestPath)}`",
            $"- TTS validation save: `{NormalisePath(savePath)}`", "",
            "Push the canonical assets before loading the validation save because it uses raw GitHub URLs."
        };
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static bool Verify(string path, AssetSeed seed) => new FileInfo(path).Length == seed.SizeBytes && HashFile(path) == seed.Sha256;
    private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string AssetUrl(string root, string path) => root + string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    private static string RepositoryFile(string root, string path) => Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Relative(string root, string path) => NormalisePath(Path.GetRelativePath(root, path));
    private static string NormalisePath(string value) => value.Replace('\\', '/');
    private static string Format(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "";
    private static string Csv(params string?[] values) => string.Join(',', values.Select(value => $"\"{(value ?? "").Replace("\"", "\"\"")}\""));
    private static string String(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: import-first-edition-core-gameplay-tokens <first-edition-repo-folder> <xwing10-save.json> [--asset-base-url <url>] [--output <folder>] [--no-download]");

    private sealed record AssetSeed(string Id, string TokenIds, string Role, string RelativeDestination, long SizeBytes, string Sha256, string SourceUrl, string? PreferredRepositoryPath);
    private sealed record TokenSeed(string Id, string Nickname, string MeshAssetId, string FaceAssetId);
    private sealed record SourceResolution(bool Success, string Resolution, string? Source, string Message);
}

public sealed class CoreGameplayTokenManifest
{
    public string SchemaVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Policy { get; init; } = "";
    public string SourceSavePath { get; init; } = "";
    public string SourceSaveSha256 { get; init; } = "";
    public int AssetCount { get; init; }
    public int TokenCount { get; init; }
    public List<ImportedCoreTokenAsset> Assets { get; init; } = [];
    public List<CoreGameplayTokenDefinition> Tokens { get; init; } = [];
}

public sealed class ImportedCoreTokenAsset
{
    public string Id { get; init; } = "";
    public List<string> TokenIds { get; init; } = [];
    public string Role { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
    public string OriginalSourceUrl { get; init; } = "";
    public string Resolution { get; init; } = "";
    public string? ResolvedFrom { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool? HasAlpha { get; init; }
    public CoreTokenMeshBounds? MeshBounds { get; init; }
}

public sealed class CoreGameplayTokenDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string MeshAssetId { get; init; } = "";
    public string FaceAssetId { get; init; } = "";
    public string MeshPath { get; init; } = "";
    public string FacePath { get; init; } = "";
    public string RuntimeStatus { get; init; } = "";
    public bool LuaIncluded { get; init; }
}

public sealed class CoreTokenMeshBounds
{
    public int VertexCount { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Depth { get; init; }
}
