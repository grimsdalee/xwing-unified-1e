using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Assets;

public static class AssetCatalogueBuilder
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".obj", ".fbx", ".dae", ".stl", ".gltf", ".glb" };

    public static AssetCatalogue Build(string repositoryFolder, string legacySavePath)
    {
        repositoryFolder = Path.GetFullPath(repositoryFolder);
        legacySavePath = Path.GetFullPath(legacySavePath);

        if (!Directory.Exists(repositoryFolder))
            throw new DirectoryNotFoundException($"Repository folder not found: {repositoryFolder}");
        if (!File.Exists(legacySavePath))
            throw new FileNotFoundException($"Legacy TTS save not found: {legacySavePath}", legacySavePath);

        var assets = new List<AssetRecord>();
        assets.AddRange(ReadRepositoryFiles(repositoryFolder));

        var game = TtsSaveLoader.Load(legacySavePath);
        foreach (var obj in game.AllObjects())
        {
            assets.Add(CreateTemplateRecord(obj, legacySavePath));
            assets.AddRange(ReadObjectUrls(obj, legacySavePath));
        }

        return new AssetCatalogue
        {
            RepositoryFolder = repositoryFolder,
            LegacySavePath = legacySavePath,
            Assets = assets
                .GroupBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Kind)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IEnumerable<AssetRecord> ReadRepositoryFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            var kind = ClassifyRepositoryFile(path, extension);
            if (kind == AssetKind.Unknown)
                continue;

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            var structural = ClassifyRepositoryStructure(relative, kind);
            var context = ExtractRepositoryContext(relative);

            yield return new AssetRecord
            {
                AssetId = StableId("repo", relative),
                Kind = kind,
                StructuralClass = structural,
                SourceKind = AssetSourceKind.RepositoryFile,
                Name = name,
                SourcePath = path,
                RelativePath = relative,
                ChassisContext = context.Chassis,
                FactionContext = context.Faction,
                SizeContext = context.Size,
                SearchTerms = AssetText.Terms(name, relative, context.Chassis, context.Faction, context.Size)
            };
        }
    }

    private static AssetKind ClassifyRepositoryFile(string path, string extension)
    {
        var normalized = AssetText.Normalize(path);
        if (ImageExtensions.Contains(extension))
        {
            if (normalized.Contains("dial")) return AssetKind.Dial;
            if (normalized.Contains("token") || normalized.Contains("marker")) return AssetKind.Token;
            if (normalized.Contains("pilot") || normalized.Contains("upgrade") || normalized.Contains("card")) return AssetKind.CardImage;
            if (normalized.Contains("base")) return AssetKind.Base;
            return AssetKind.Image;
        }
        if (ModelExtensions.Contains(extension))
        {
            if (normalized.Contains("base")) return AssetKind.Base;
            return AssetKind.ShipModel;
        }
        if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase)) return AssetKind.Script;
        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)) return AssetKind.Xml;
        return AssetKind.Unknown;
    }

    private static AssetStructuralClass ClassifyRepositoryStructure(string relativePath, AssetKind kind)
    {
        var text = AssetText.Normalize(relativePath);
        if (kind == AssetKind.ShipModel) return AssetStructuralClass.ShipModel;
        if (kind == AssetKind.Base)
        {
            if (text.Contains("huge")) return AssetStructuralClass.SharedHugeBase;
            if (text.Contains("large")) return AssetStructuralClass.SharedLargeBase;
            return AssetStructuralClass.SharedSmallBase;
        }
        if (kind == AssetKind.Dial) return AssetStructuralClass.DialObject;
        if (kind == AssetKind.Token) return AssetStructuralClass.Token;
        if (kind == AssetKind.Script) return AssetStructuralClass.Script;
        if (kind == AssetKind.Xml) return AssetStructuralClass.Xml;
        if (kind == AssetKind.CardImage)
        {
            if (text.Contains("epiccards") || text.Contains("upgradecards") || text.Contains("upgrade") || text.Contains("upgrades"))
                return AssetStructuralClass.UpgradeCardImage;
            return AssetStructuralClass.PilotCardImage;
        }
        if (kind == AssetKind.Image)
        {
            if (text.Contains("epiccards") || text.Contains("upgradecards") || text.Contains("upgrades"))
                return AssetStructuralClass.UpgradeCardImage;
            if (text.Contains("pilotcards") || text.Contains("pilots"))
                return AssetStructuralClass.PilotCardImage;
            if (text.Contains("ships") || text.Contains("shipstv2") || text.Contains("textures"))
                return AssetStructuralClass.ShipTexture;
        }
        return AssetStructuralClass.Unknown;
    }

    private static (string Chassis, string Faction, string Size) ExtractRepositoryContext(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = parts.Select(AssetText.Normalize).ToArray();
        var size = normalized.FirstOrDefault(x => x is "small" or "medium" or "large" or "huge") ?? "";
        var faction = normalized.FirstOrDefault(x => x is "rebel" or "empire" or "scum" or "firstorder" or "resistance" or "republic" or "cis") ?? "";
        var shipsIndex = Array.FindIndex(normalized, x => x is "ships" or "shipsv2");
        var chassis = shipsIndex >= 0 && shipsIndex + 1 < normalized.Length
            ? normalized.Skip(shipsIndex + 1).FirstOrDefault(x => x is not ("small" or "medium" or "large" or "huge" or "rebel" or "empire" or "scum" or "firstorder" or "resistance" or "republic" or "cis" or "textures" or "texture" or "old" or "v2" or "v3" or "1")) ?? ""
            : "";
        return (chassis, faction, size);
    }

    private static AssetRecord CreateTemplateRecord(TtsObject obj, string savePath)
    {
        var display = FirstNonEmpty(obj.Nickname, obj.Name, obj.Type, obj.Guid);
        var structural = ClassifyTemplate(obj, display);
        return new AssetRecord
        {
            AssetId = StableId("tts-object", obj.Guid, display),
            Kind = AssetKind.Template,
            StructuralClass = structural,
            SourceKind = AssetSourceKind.LegacySaveObject,
            Name = display,
            SourcePath = savePath,
            TtsGuid = obj.Guid,
            TtsType = obj.Type,
            ParentGuid = obj.Parent?.Guid ?? "",
            TemplateJson = obj.Json?.ToJsonString() ?? "",
            SearchTerms = AssetText.Terms(
                new[] { display, obj.Description, obj.GMNotes, obj.Type, structural.ToString() }
                    .Concat(DescendantSearchValues(obj))
                    .ToArray())
        };
    }

    private static AssetStructuralClass ClassifyTemplate(TtsObject obj, string display)
    {
        var text = AssetText.Normalize(string.Join(" ", display, obj.Type, obj.Description, obj.GMNotes));
        var type = obj.Type ?? "";
        if (text.Contains("dial"))
            return type.Contains("Bag", StringComparison.OrdinalIgnoreCase) ? AssetStructuralClass.DialBag : AssetStructuralClass.DialObject;
        if (type.Contains("Card", StringComparison.OrdinalIgnoreCase)
            || type.Contains("Deck", StringComparison.OrdinalIgnoreCase)
            || text.Contains("card")
            || obj.Children.Any(child => child.IsCard || child.IsDeck)
            || obj.AllChildren().Any(child => child.IsCard || child.IsDeck))
            return AssetStructuralClass.CardObjectTemplate;
        if (text.Contains("base"))
        {
            if (text.Contains("huge")) return AssetStructuralClass.SharedHugeBase;
            if (text.Contains("large")) return AssetStructuralClass.SharedLargeBase;
            return AssetStructuralClass.SharedSmallBase;
        }
        if (type.Contains("Model", StringComparison.OrdinalIgnoreCase) && !type.Contains("Bag", StringComparison.OrdinalIgnoreCase))
            return AssetStructuralClass.ShipObjectTemplate;
        return AssetStructuralClass.Unknown;
    }


    private static IEnumerable<string?> DescendantSearchValues(TtsObject obj)
    {
        foreach (var child in obj.AllChildren())
        {
            yield return child.Nickname;
            yield return child.Name;
            yield return child.Description;
            yield return child.GMNotes;
            yield return child.Type;

            if (child.Json is not null)
            {
                foreach (var value in EnumerateStrings(child.Json, ""))
                {
                    if (!Uri.TryCreate(value.Value, UriKind.Absolute, out _))
                        yield return value.Value;
                }
            }
        }
    }

    private static IEnumerable<AssetRecord> ReadObjectUrls(TtsObject obj, string savePath)
    {
        foreach (var found in EnumerateStrings(obj.Json, ""))
        {
            if (!Uri.TryCreate(found.Value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                continue;

            var kind = ClassifyUrl(found.Pointer, found.Value, obj);
            var filename = Path.GetFileName(uri.LocalPath);
            var display = FirstNonEmpty(filename, obj.Nickname, obj.Name, found.Pointer);
            var structural = ClassifyUrlStructure(found.Pointer, kind, obj, display);

            yield return new AssetRecord
            {
                AssetId = StableId("tts-url", found.Value),
                Kind = kind,
                StructuralClass = structural,
                SourceKind = AssetSourceKind.LegacySaveUrl,
                Name = display,
                SourcePath = savePath,
                Url = found.Value,
                TtsGuid = obj.Guid,
                TtsType = obj.Type,
                ParentGuid = obj.Parent?.Guid ?? "",
                JsonPointer = found.Pointer,
                SearchTerms = AssetText.Terms(display, obj.Nickname, obj.Description, obj.GMNotes, found.Pointer, found.Value, structural.ToString())
            };
        }
    }

    private static AssetKind ClassifyUrl(string pointer, string value, TtsObject obj)
    {
        var text = AssetText.Normalize(pointer + " " + value + " " + obj.Nickname + " " + obj.Name);
        if (text.Contains("meshurl") || text.Contains("colliderurl")) return AssetKind.ShipModel;
        if (text.Contains("diffuseurl") && text.Contains("base")) return AssetKind.Base;
        if (text.Contains("faceurl") || text.Contains("backurl") || text.Contains("imageurl")) return AssetKind.CardImage;
        if (text.Contains("dial")) return AssetKind.Dial;
        if (text.Contains("token") || text.Contains("marker")) return AssetKind.Token;
        if (text.Contains("base")) return AssetKind.Base;
        return AssetKind.Image;
    }

    private static AssetStructuralClass ClassifyUrlStructure(string pointer, AssetKind kind, TtsObject obj, string display)
    {
        var text = AssetText.Normalize(string.Join(" ", pointer, display, obj.Nickname, obj.Name, obj.Type));
        if (kind == AssetKind.ShipModel) return AssetStructuralClass.ShipModel;
        if (kind == AssetKind.Base)
        {
            if (text.Contains("huge")) return AssetStructuralClass.SharedHugeBase;
            if (text.Contains("large")) return AssetStructuralClass.SharedLargeBase;
            return AssetStructuralClass.SharedSmallBase;
        }
        if (kind == AssetKind.Dial) return AssetStructuralClass.DialObject;
        if (kind == AssetKind.CardImage)
        {
            if (text.Contains("upgrade")) return AssetStructuralClass.UpgradeCardImage;
            if (text.Contains("pilot")) return AssetStructuralClass.PilotCardImage;
            return AssetStructuralClass.Unknown;
        }
        return AssetStructuralClass.Unknown;
    }

    private static IEnumerable<(string Pointer, string Value)> EnumerateStrings(JsonNode? node, string pointer)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                var childPointer = pointer + "/" + EscapePointer(pair.Key);
                foreach (var item in EnumerateStrings(pair.Value, childPointer))
                    yield return item;
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
                foreach (var item in EnumerateStrings(array[i], pointer + "/" + i))
                    yield return item;
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            yield return (pointer, text);
    }

    private static string EscapePointer(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Unnamed asset";

    private static string StableId(params string[] values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values)));
        return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }
}
