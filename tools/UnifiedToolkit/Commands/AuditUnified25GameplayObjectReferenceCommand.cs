using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

public static class AuditUnified25GameplayObjectReferenceCommand
{
    private const string Unified25RawPrefix = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/";
    private const string Unified1eRawPrefix = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Unified 2.5 Gameplay Object Reference Audit");
        Console.WriteLine("========================================================");
        Console.WriteLine();

        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: audit-unified25-gameplay-object-reference <first-edition-repo-folder> <tts-save.json> [--output <folder>]");
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var savePath = Path.GetFullPath(args[1]);
            var output = Path.Combine(repository, "_unifiedtoolkit_reports", "phase16", "unified25-gameplay-object-reference");

            for (var index = 2; index < args.Length; index++)
            {
                if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    output = Path.GetFullPath(args[++index]);
                    continue;
                }

                throw new ArgumentException($"Unknown or incomplete option: {args[index]}");
            }

            if (!Directory.Exists(repository))
                throw new DirectoryNotFoundException($"Repository not found: {repository}");
            if (!File.Exists(savePath))
                throw new FileNotFoundException("TTS reference save not found.", savePath);

            Directory.CreateDirectory(output);
            using var source = JsonDocument.Parse(File.ReadAllBytes(savePath));
            if (!source.RootElement.TryGetProperty("ObjectStates", out var states) || states.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The TTS save does not contain an ObjectStates array.");

            var objects = new List<ReferenceObject>();
            var assets = new Dictionary<string, AssetBuilder>(StringComparer.OrdinalIgnoreCase);
            var topLevelIndex = 0;
            foreach (var state in states.EnumerateArray())
            {
                InspectObject(state, $"ObjectStates[{topLevelIndex}]", true, repository, objects, assets);
                topLevelIndex++;
            }

            var assetRows = assets.Values
                .Select(asset => asset.Build())
                .OrderBy(asset => asset.Url, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var relevant = objects.Where(item => item.Policy != "excluded-second-edition").ToList();
            var exclusions = objects.Where(item => item.Policy == "excluded-second-edition").ToList();
            var targetLock = objects.Any(item => item.Category == "target-lock");
            var ordnance = objects.Any(item => item.Category == "ordnance");

            var report = new ReferenceReport
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Repository = repository,
                SourceSave = savePath,
                SourceSaveBytes = new FileInfo(savePath).Length,
                SourceSaveSha256 = HashFile(savePath),
                TopLevelObjectCount = topLevelIndex,
                RelevantObjectCount = relevant.Count,
                ReusableCandidateCount = relevant.Count(item => item.Policy is "candidate-reuse" or "approved-compatibility"),
                ReviewRequiredCount = relevant.Count(item => item.Policy == "review-required"),
                SecondEditionExclusionCount = exclusions.Count,
                ResolvedLocalAssetCount = assetRows.Count(item => item.LocalFileExists),
                UnresolvedOrExternalAssetCount = assetRows.Count(item => !item.LocalFileExists),
                RasterAssetCount = assetRows.Count(item => item.RasterWidth.HasValue),
                LowResolutionRasterAssetCount = assetRows.Count(item => item.ResolutionBand == "low"),
                TargetLockFound = targetLock,
                OrdnanceFound = ordnance,
                Objects = objects.OrderBy(item => item.SourcePath, StringComparer.Ordinal).ToList(),
                Assets = assetRows,
                ArchitectureDecisions = BuildDecisions(targetLock, ordnance)
            };

            var jsonPath = Path.Combine(output, "unified25-gameplay-object-reference.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            WriteObjectCsv(Path.Combine(output, "unified25-gameplay-object-reference-objects.csv"), report.Objects);
            WriteAssetCsv(Path.Combine(output, "unified25-gameplay-object-reference-assets.csv"), report.Assets);
            WriteQualityCsv(Path.Combine(output, "unified25-gameplay-object-texture-review.csv"), report.Assets);
            File.WriteAllText(Path.Combine(output, "FIRST-EDITION-UNIFIED25-GAMEPLAY-OBJECT-REFERENCE.md"), BuildMarkdown(report));

            Console.WriteLine($"Repository:                      {repository}");
            Console.WriteLine($"Reference save:                  {savePath}");
            Console.WriteLine($"Top-level objects:               {report.TopLevelObjectCount}");
            Console.WriteLine($"First Edition-relevant objects:  {report.RelevantObjectCount}");
            Console.WriteLine($"Reusable candidates:             {report.ReusableCandidateCount}");
            Console.WriteLine($"Review required:                 {report.ReviewRequiredCount}");
            Console.WriteLine($"Second Edition exclusions:       {report.SecondEditionExclusionCount}");
            Console.WriteLine($"Resolved local assets:           {report.ResolvedLocalAssetCount}");
            Console.WriteLine($"Unresolved/external assets:      {report.UnresolvedOrExternalAssetCount}");
            Console.WriteLine($"Raster assets:                   {report.RasterAssetCount}");
            Console.WriteLine($"Low-resolution raster assets:    {report.LowResolutionRasterAssetCount}");
            Console.WriteLine($"Target Lock found:               {report.TargetLockFound}");
            Console.WriteLine($"Ordnance found:                  {report.OrdnanceFound}");
            Console.WriteLine();
            Console.WriteLine($"Inventory: {jsonPath}");
            Console.WriteLine($"Report:    {Path.Combine(output, "FIRST-EDITION-UNIFIED25-GAMEPLAY-OBJECT-REFERENCE.md")}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. No assets, mappings, Lua scripts or gameplay state were modified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Audit failed: {exception.Message}");
            return 1;
        }
    }

    private static void InspectObject(
        JsonElement item,
        string sourcePath,
        bool isTopLevel,
        string repository,
        List<ReferenceObject> objects,
        Dictionary<string, AssetBuilder> assets)
    {
        var nickname = Text(item, "Nickname");
        var type = Text(item, "Name");
        var urls = EnumerateAssetReferences(item).ToList();
        var combined = string.Join(' ', new[] { nickname, type }.Concat(urls.Select(value => value.Url))).ToLowerInvariant();
        var classification = Classify(nickname, type, combined);

        if (isTopLevel && classification is not null && !IsContainer(type))
        {
            var guid = Text(item, "GUID");
            var lua = Text(item, "LuaScript");
            var objectAssets = new List<string>();
            foreach (var asset in urls)
            {
                objectAssets.Add(asset.Url);
                AddAsset(assets, asset.Url, asset.Role, guid, sourcePath, repository);
            }

            var statesCount = item.TryGetProperty("States", out var states) && states.ValueKind == JsonValueKind.Object
                ? states.EnumerateObject().Count()
                : 0;
            if (statesCount > 0)
            {
                foreach (var state in states.EnumerateObject())
                foreach (var asset in EnumerateAssetReferences(state.Value))
                {
                    objectAssets.Add(asset.Url);
                    AddAsset(assets, asset.Url, $"State {state.Name} {asset.Role}", guid, sourcePath, repository);
                }
            }

            objects.Add(new ReferenceObject
            {
                SourcePath = sourcePath,
                Guid = guid,
                Nickname = nickname,
                TtsObjectType = type,
                Category = classification.Value.Category,
                Policy = classification.Value.Policy,
                Recommendation = classification.Value.Recommendation,
                ScaleX = Number(item, "Transform", "scaleX"),
                ScaleY = Number(item, "Transform", "scaleY"),
                ScaleZ = Number(item, "Transform", "scaleZ"),
                StateCount = statesCount,
                LuaPresent = !string.IsNullOrWhiteSpace(lua),
                LuaLength = lua.Length,
                LuaSha256 = string.IsNullOrEmpty(lua) ? null : HashText(lua),
                AssetUrls = objectAssets.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList()
            });
        }

        if (item.TryGetProperty("ContainedObjects", out var contained) && contained.ValueKind == JsonValueKind.Array)
        {
            var childIndex = 0;
            foreach (var child in contained.EnumerateArray())
            {
                InspectObject(child, $"{sourcePath}.ContainedObjects[{childIndex}]", false, repository, objects, assets);
                childIndex++;
            }
        }
    }

    private static (string Category, string Policy, string Recommendation)? Classify(string nickname, string type, string combined)
    {
        if (ContainsAny(nickname, "version check", "dial set source", "movelut", "dicepreload") ||
            (string.IsNullOrWhiteSpace(nickname) && combined.Contains("custom-dice-image.png", StringComparison.OrdinalIgnoreCase)))
            return null;
        if (ContainsAny(combined, "calculate", "force token", "charge token", "strain", "deplete", "fuse token", "gas cloud", "remote", "electro-proton bomb", "concussion bomb", "blaze bomb"))
            return ("second-edition-only", "excluded-second-edition", "Exclude from First Edition unless a later reviewed requirement explicitly adds it.");
        if (ContainsAny(combined, "target lock", "/tl.obj", "/tl.png"))
            return ("target-lock", "approved-compatibility", "Reuse the Unified 2.5 single owner-labelled token, mesh, texture and assignment integration while enforcing First Edition targeting rules.");
        if (ContainsAny(combined, "ordnance"))
            return ("ordnance", "review-required", "No Unified 2.5 runtime equivalent is expected; assess the canonical First Edition/Vassal candidate separately.");
        if (ContainsAny(combined, "focus", "evade", "stress", "ion token", "cloak", "jam token", "tractor", "disarm", "weapons disabled", "reinforce", "shield", "energy token", "critical hit"))
            return ("token", "candidate-reuse", "Reuse object construction, physical scale and compatible runtime behaviour; review the face texture against First Edition artwork.");
        if (ContainsAny(combined, "bomblet", "bomb", "seismic charge", "thermal detonator", "proximity mine", "cluster mine", "conner net", "connor net", "loose cargo"))
            return ("device", "candidate-reuse", "Reuse geometry, scale and compatible placement behaviour after First Edition rules and artwork review.");
        if (ContainsAny(combined, "asteroid", "debrisfield", "debris field"))
            return ("obstacle", "candidate-reuse", "Reuse the established obstacle mesh, collider, scale and texture when it represents a First Edition component.");
        if (ContainsAny(combined, "bank 1", "bank 2", "bank 3", "turn 1", "turn 2", "turn 3", "straight 1", "straight 2", "straight 3", "straight 4", "straight 5", "range ruler", "movement ruler", "maneuver template", "movement template"))
            return ("ruler-template", "candidate-reuse", "Reuse the Unified 2.5 physical dimensions and mesh; verify First Edition markings and supported maneuvers.");
        if (type.Contains("Dice", StringComparison.OrdinalIgnoreCase) || ContainsAny(combined, "attack die", "defence die", "defense die", "reddie", "greendie"))
            return ("dice", "candidate-reuse", "Reuse the compatible attack or defence die construction after face-map verification.");
        if (ContainsAny(combined, "first player", "initiative token"))
            return ("optional-player-aid", "review-required", "Retain as an optional player aid only; it is not integral to First Edition runtime.");
        return null;
    }

    private static IEnumerable<(string Role, string Url)> EnumerateAssetReferences(JsonElement item)
    {
        if (item.TryGetProperty("CustomMesh", out var mesh) && mesh.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in new[] { ("Mesh", "MeshURL"), ("Diffuse", "DiffuseURL"), ("Normal", "NormalURL"), ("Collider", "ColliderURL") })
            {
                var value = Text(mesh, pair.Item2);
                if (IsRemote(value)) yield return (pair.Item1, value);
            }
        }

        if (item.TryGetProperty("CustomImage", out var image) && image.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in new[] { ("Image", "ImageURL"), ("Secondary image", "ImageSecondaryURL") })
            {
                var value = Text(image, pair.Item2);
                if (IsRemote(value)) yield return (pair.Item1, value);
            }
        }
    }

    private static void AddAsset(Dictionary<string, AssetBuilder> assets, string url, string role, string guid, string sourcePath, string repository)
    {
        if (!assets.TryGetValue(url, out var asset))
        {
            asset = new AssetBuilder(url, ResolveLocalPath(url, repository));
            assets.Add(url, asset);
        }
        asset.References.Add($"{guid}|{sourcePath}|{role}");
    }

    private static string? ResolveLocalPath(string url, string repository)
    {
        string? relative = null;
        if (url.StartsWith(Unified25RawPrefix, StringComparison.OrdinalIgnoreCase))
            relative = Path.Combine("assets", "source", "unified25", Uri.UnescapeDataString(url[Unified25RawPrefix.Length..]));
        else if (url.StartsWith(Unified1eRawPrefix, StringComparison.OrdinalIgnoreCase))
            relative = Uri.UnescapeDataString(url[Unified1eRawPrefix.Length..]);

        if (relative is null) return null;
        var resolved = Path.GetFullPath(Path.Combine(repository, relative.Replace('/', Path.DirectorySeparatorChar)));
        return resolved.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? resolved : null;
    }

    private static List<ArchitectureDecision> BuildDecisions(bool targetLockFound, bool ordnanceFound) =>
    [
        new() { Subject = "Target Lock", Status = "approved", Decision = "Reuse Unified 2.5's single token, owner/pilot label, image and assignment integration. First Edition range, acquisition and spending rules remain authoritative.", EvidenceFound = targetLockFound },
        new() { Subject = "Ordnance", Status = "separate-first-edition-source", Decision = "Unified 2.5 has no Ordnance token. Review the Vassal Token-ordnance.png candidate separately; do not synthesize or silently substitute artwork.", EvidenceFound = ordnanceFound },
        new() { Subject = "Physical construction", Status = "reference-only", Decision = "Treat mesh, collider, transform scale, state layout and texture dimensions as reusable engineering evidence, not automatic approval of Second Edition artwork or rules.", EvidenceFound = true },
        new() { Subject = "Second Edition exclusions", Status = "excluded", Decision = "Calculate, Force, Charge, Strain, Deplete, Fuse, gas-cloud, remote, Electro-Proton Bomb, Concussion Bomb and Blaze Bomb objects are excluded unless a later reviewed First Edition requirement explicitly needs one.", EvidenceFound = true }
    ];

    private static string BuildMarkdown(ReferenceReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# First Edition Unified 2.5 Gameplay Object Reference");
        builder.AppendLine();
        builder.AppendLine("This is a read-only engineering audit of the supplied spawned-object save. It does not approve or import assets and does not change gameplay.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Relevant objects: {report.RelevantObjectCount}");
        builder.AppendLine($"- Reusable candidates: {report.ReusableCandidateCount}");
        builder.AppendLine($"- Review required: {report.ReviewRequiredCount}");
        builder.AppendLine($"- Second Edition exclusions: {report.SecondEditionExclusionCount}");
        builder.AppendLine($"- Locally resolved assets: {report.ResolvedLocalAssetCount}");
        builder.AppendLine($"- Low-resolution raster assets: {report.LowResolutionRasterAssetCount}");
        builder.AppendLine($"- Target Lock found: {report.TargetLockFound}");
        builder.AppendLine($"- Ordnance found: {report.OrdnanceFound}");
        builder.AppendLine();
        builder.AppendLine("Resolution bands are descriptive rather than automatic quality verdicts: low is below 128 pixels on the shortest side, moderate is 128–255, and high is 256 or greater. In-game scale and mesh UV usage still require visual review.");
        builder.AppendLine();
        builder.AppendLine("## Architecture decisions");
        builder.AppendLine();
        foreach (var decision in report.ArchitectureDecisions)
            builder.AppendLine($"- **{decision.Subject} ({decision.Status}):** {decision.Decision}");
        builder.AppendLine();
        builder.AppendLine("## Category totals");
        builder.AppendLine();
        builder.AppendLine("| Category | Objects |");
        builder.AppendLine("|---|---:|");
        foreach (var group in report.Objects.GroupBy(item => item.Category).OrderBy(group => group.Key))
            builder.AppendLine($"| {group.Key} | {group.Count()} |");
        return builder.ToString();
    }

    private static void WriteObjectCsv(string path, IEnumerable<ReferenceObject> rows)
    {
        var builder = new StringBuilder("sourcePath,guid,nickname,ttsObjectType,category,policy,scaleX,scaleY,scaleZ,stateCount,luaPresent,luaLength,luaSha256,assetCount,recommendation\r\n");
        foreach (var row in rows)
            builder.AppendLine(Csv(row.SourcePath, row.Guid, row.Nickname, row.TtsObjectType, row.Category, row.Policy, Format(row.ScaleX), Format(row.ScaleY), Format(row.ScaleZ), row.StateCount.ToString(), row.LuaPresent.ToString(), row.LuaLength.ToString(), row.LuaSha256, row.AssetUrls.Count.ToString(), row.Recommendation));
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    private static void WriteAssetCsv(string path, IEnumerable<ReferenceAsset> rows)
    {
        var builder = new StringBuilder("url,localPath,localFileExists,bytes,sha256,extension,rasterWidth,rasterHeight,hasAlpha,resolutionBand,referenceCount,references\r\n");
        foreach (var row in rows)
            builder.AppendLine(Csv(row.Url, row.LocalPath, row.LocalFileExists.ToString(), row.Bytes?.ToString(), row.Sha256, row.Extension, row.RasterWidth?.ToString(), row.RasterHeight?.ToString(), row.HasAlpha?.ToString(), row.ResolutionBand, row.ReferenceCount.ToString(), string.Join("; ", row.References)));
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    private static void WriteQualityCsv(string path, IEnumerable<ReferenceAsset> rows)
    {
        var builder = new StringBuilder("resolutionBand,width,height,hasAlpha,bytes,localFileExists,url,localPath\r\n");
        foreach (var row in rows.Where(row => IsRasterExtension(row.Extension)).OrderBy(row => ResolutionOrder(row.ResolutionBand)).ThenBy(row => row.RasterWidth))
            builder.AppendLine(Csv(row.ResolutionBand, row.RasterWidth?.ToString(), row.RasterHeight?.ToString(), row.HasAlpha?.ToString(), row.Bytes?.ToString(), row.LocalFileExists.ToString(), row.Url, row.LocalPath));
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    private static bool IsContainer(string type) => type.Contains("Bag", StringComparison.OrdinalIgnoreCase) || type.Contains("Deck", StringComparison.OrdinalIgnoreCase);
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool IsRemote(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static double? Number(JsonElement item, string parent, string name) => item.TryGetProperty(parent, out var container) && container.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
    private static string Format(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "";
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Csv(params string?[] values) => string.Join(',', values.Select(value => $"\"{(value ?? "").Replace("\"", "\"\"")}\""));
    private static bool IsRasterExtension(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".webp";
    private static int ResolutionOrder(string value) => value switch { "low" => 0, "moderate" => 1, "high" => 2, _ => 3 };

    private sealed class AssetBuilder
    {
        public AssetBuilder(string url, string? localPath) { Url = url; LocalPath = localPath; }
        public string Url { get; }
        public string? LocalPath { get; }
        public HashSet<string> References { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ReferenceAsset Build()
        {
            var exists = LocalPath is not null && File.Exists(LocalPath);
            long? bytes = null;
            string? sha256 = null;
            int? width = null;
            int? height = null;
            bool? alpha = null;
            var extension = Path.GetExtension(Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : Url).ToLowerInvariant();
            if (exists)
            {
                bytes = new FileInfo(LocalPath!).Length;
                sha256 = HashFile(LocalPath!);
                if (IsRasterExtension(extension))
                {
                    using var bitmap = SKBitmap.Decode(LocalPath!);
                    if (bitmap is not null)
                    {
                        width = bitmap.Width;
                        height = bitmap.Height;
                        alpha = bitmap.AlphaType != SKAlphaType.Opaque;
                    }
                }
            }
            var minimum = width.HasValue && height.HasValue ? Math.Min(width.Value, height.Value) : (int?)null;
            var band = minimum switch { < 128 => "low", < 256 => "moderate", >= 256 => "high", _ => "unknown" };
            return new ReferenceAsset { Url = Url, LocalPath = LocalPath, LocalFileExists = exists, Bytes = bytes, Sha256 = sha256, Extension = extension, RasterWidth = width, RasterHeight = height, HasAlpha = alpha, ResolutionBand = band, ReferenceCount = References.Count, References = References.Order().ToList() };
        }
    }
}

public sealed class ReferenceReport
{
    public int SchemaVersion { get; set; }
    public string GeneratedUtc { get; set; } = "";
    public string Repository { get; set; } = "";
    public string SourceSave { get; set; } = "";
    public long SourceSaveBytes { get; set; }
    public string SourceSaveSha256 { get; set; } = "";
    public int TopLevelObjectCount { get; set; }
    public int RelevantObjectCount { get; set; }
    public int ReusableCandidateCount { get; set; }
    public int ReviewRequiredCount { get; set; }
    public int SecondEditionExclusionCount { get; set; }
    public int ResolvedLocalAssetCount { get; set; }
    public int UnresolvedOrExternalAssetCount { get; set; }
    public int RasterAssetCount { get; set; }
    public int LowResolutionRasterAssetCount { get; set; }
    public bool TargetLockFound { get; set; }
    public bool OrdnanceFound { get; set; }
    public List<ReferenceObject> Objects { get; set; } = [];
    public List<ReferenceAsset> Assets { get; set; } = [];
    public List<ArchitectureDecision> ArchitectureDecisions { get; set; } = [];
}

public sealed class ReferenceObject
{
    public string SourcePath { get; set; } = "";
    public string Guid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string TtsObjectType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Policy { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public double? ScaleX { get; set; }
    public double? ScaleY { get; set; }
    public double? ScaleZ { get; set; }
    public int StateCount { get; set; }
    public bool LuaPresent { get; set; }
    public int LuaLength { get; set; }
    public string? LuaSha256 { get; set; }
    public List<string> AssetUrls { get; set; } = [];
}

public sealed class ReferenceAsset
{
    public string Url { get; set; } = "";
    public string? LocalPath { get; set; }
    public bool LocalFileExists { get; set; }
    public long? Bytes { get; set; }
    public string? Sha256 { get; set; }
    public string Extension { get; set; } = "";
    public int? RasterWidth { get; set; }
    public int? RasterHeight { get; set; }
    public bool? HasAlpha { get; set; }
    public string ResolutionBand { get; set; } = "unknown";
    public int ReferenceCount { get; set; }
    public List<string> References { get; set; } = [];
}

public sealed class ArchitectureDecision
{
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "";
    public string Decision { get; set; } = "";
    public bool EvidenceFound { get; set; }
}
