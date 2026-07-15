using System.Security.Cryptography;
using System.Text;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Hybrid;

public static class SpawnerFrameworkAnalyser
{
    private static readonly string[] UtilityTerms =
    {
        "accessor", "dice", "damage", "objective", "obstacle", "targetlock", "token",
        "hyperspace", "version", "marker", "ancientknowledge", "criticalhit", "extraassets"
    };

    public static IReadOnlyList<SpawnerFrameworkReference> Analyse(string unifiedSavePath)
    {
        var game = TtsSaveLoader.Load(unifiedSavePath);
        return game.AllObjects()
            .Select(CreateCandidate)
            .Where(x => x.StructuralScore >= 8 && !x.IsUtilityObject)
            .OrderByDescending(x => x.StructuralScore)
            .ThenBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SpawnerFrameworkReference? SelectForSize(
        IReadOnlyList<SpawnerFrameworkReference> frameworks,
        string size)
    {
        var normalizedSize = HybridText.Normalize(size);
        return frameworks
            .Select(x => new { Framework = x, Score = SizeScore(x, normalizedSize) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Framework.StructuralScore)
            .Select(x => x.Framework)
            .FirstOrDefault();
    }

    private static SpawnerFrameworkReference CreateCandidate(TtsObject obj)
    {
        var descendants = obj.AllChildren().ToArray();
        var all = new[] { obj }.Concat(descendants).ToArray();
        var name = FirstNonEmpty(obj.Nickname, obj.Description, obj.Name, obj.Guid);
        var path = ObjectPath(obj);
        var directText = HybridText.Normalize(name + " " + obj.Description + " " + obj.GMNotes);
        var subtreeText = HybridText.Normalize(string.Join(' ', all.Select(x => FirstNonEmpty(x.Nickname, x.Description, x.Name))));
        var isUtility = UtilityTerms.Any(term => directText.Contains(term));

        var hasLua = all.Any(x => x.HasLua);
        var hasSnapPoints = all.Any(x => x.Json["SnapPoints"] is System.Text.Json.Nodes.JsonArray points && points.Count > 0);
        var hasContained = obj.Children.Count > 0;
        var hasBase = subtreeText.Contains("base");
        var hasPeg = subtreeText.Contains("peg") || subtreeText.Contains("stem");
        var hasShipModel = subtreeText.Contains("ship") || subtreeText.Contains("model");
        var hasSpawnerLanguage = subtreeText.Contains("spawn") || subtreeText.Contains("launcher");
        var size = DetectSize(directText + subtreeText);

        var score = 0;
        if (hasLua) score += 4;
        if (hasSnapPoints) score += 4;
        if (hasContained) score += 2;
        if (hasBase) score += 2;
        if (hasPeg) score += 2;
        if (hasShipModel) score += 2;
        if (hasSpawnerLanguage) score += 2;
        if (!string.IsNullOrWhiteSpace(size)) score += 3;
        if (descendants.Length >= 3) score += 1;
        if (isUtility) score -= 20;

        return new SpawnerFrameworkReference
        {
            FrameworkId = StableId("framework", obj.Guid, name),
            Size = size,
            SourceGuid = obj.Guid,
            SourceName = name,
            SourcePath = path,
            HasLua = hasLua,
            HasSnapPoints = hasSnapPoints,
            HasContainedObjects = hasContained,
            HasBaseComponent = hasBase,
            HasPegComponent = hasPeg,
            HasShipAttachment = hasShipModel,
            HasSpawnerBehaviour = hasSpawnerLanguage,
            DescendantCount = descendants.Length,
            IsUtilityObject = isUtility,
            StructuralScore = score,
            TemplateJson = obj.Json.ToJsonString()
        };
    }

    private static int SizeScore(SpawnerFrameworkReference framework, string size)
    {
        if (string.IsNullOrWhiteSpace(size)) return 0;
        var frameworkSize = HybridText.Normalize(framework.Size);
        if (frameworkSize == size) return 100;
        var text = HybridText.Normalize(framework.SourceName + " " + framework.SourcePath);
        return text.Contains(size) ? 80 : 0;
    }

    private static string DetectSize(string text)
    {
        foreach (var size in new[] { "huge", "large", "medium", "small" })
            if (text.Contains(size)) return size;
        return "";
    }

    internal static string ObjectPath(TtsObject obj)
    {
        var parts = new Stack<string>();
        for (var current = obj; current is not null; current = current.Parent)
            parts.Push(FirstNonEmpty(current.Nickname, current.Name, current.Guid));
        return string.Join(" / ", parts);
    }

    internal static string StableId(params string[] values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', values)));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
}
