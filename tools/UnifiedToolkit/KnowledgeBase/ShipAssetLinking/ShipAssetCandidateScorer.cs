using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetCandidateScorer
{
    private readonly LegacyAssetContextMatcher contextMatcher;

    public ShipAssetCandidateScorer(LegacyAssetContextMatcher contextMatcher)
    {
        this.contextMatcher = contextMatcher;
    }

    public KnowledgeBaseShipAssetCandidate Score(
        KnowledgeBaseAsset asset,
        string role,
        ShipIdentityProfile identity,
        IReadOnlyList<LegacyAssetContext> contexts,
        string baseSize)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(identity);

        var path = ShipAssetText.Normalize(asset.RepositoryPath);
        var compactPath = ShipAssetText.Compact(path);
        var compactSegments = SplitCompactSegments(path);
        var score = 0;
        var reasons = new List<string>();

        var strongMatch = FindBestMatch(identity.StrongAliases, compactPath, compactSegments, true);
        score += strongMatch.Points;
        if (strongMatch.Points > 0)
        {
            reasons.Add(strongMatch.Reason);
        }

        if (strongMatch.Points == 0)
        {
            var weakMatch = FindBestMatch(identity.WeakAliases, compactPath, compactSegments, false);
            score += weakMatch.Points;
            if (weakMatch.Points > 0)
            {
                reasons.Add(weakMatch.Reason);
            }
        }

        var contextMatch = contextMatcher.FindBestMatch(contexts, role, identity, baseSize);
        score += contextMatch.Points;
        if (contextMatch.Points > 0)
        {
            reasons.Add(contextMatch.Reason);
        }

        if (path.Contains("/ships-v2/", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
            reasons.Add("ships-v2 location");
        }
        else if (path.Contains("/ships/", StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
            reasons.Add("ships location");
        }

        var roleBonus = GetRoleBonus(path, asset.Extension, role);
        score += roleBonus.Points;
        if (roleBonus.Points != 0)
        {
            reasons.Add(roleBonus.Reason);
        }

        if (asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase)
            && role is "BaseToken" or "DialTexture"
            && contextMatch.Points > 0)
        {
            score += 12;
            reasons.Add("First Edition contextual asset preference");
        }

        var boundedScore = Math.Clamp(score, 0, 100);

        return new KnowledgeBaseShipAssetCandidate
        {
            AssetId = asset.AssetId,
            RepositoryPath = asset.RepositoryPath,
            Warehouse = asset.Warehouse,
            Score = boundedScore,
            Confidence = boundedScore >= 85 ? "high" : boundedScore >= 60 ? "medium" : "low",
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static (int Points, string Reason) FindBestMatch(
        IEnumerable<string> aliases,
        string compactPath,
        IReadOnlyCollection<string> compactSegments,
        bool strong)
    {
        var bestPoints = 0;
        var bestReason = string.Empty;

        foreach (var alias in aliases.OrderByDescending(item => item.Length))
        {
            var compactAlias = ShipAssetText.Compact(alias);
            if (compactAlias.Length < 3)
            {
                continue;
            }

            int points;
            string matchType;

            if (compactSegments.Contains(compactAlias, StringComparer.OrdinalIgnoreCase))
            {
                points = strong ? 78 : 28;
                matchType = "exact path segment";
            }
            else if (compactPath.Contains(compactAlias, StringComparison.OrdinalIgnoreCase))
            {
                points = strong
                    ? compactAlias.Length >= 10 ? 62 : compactAlias.Length >= 6 ? 52 : 38
                    : compactAlias.Length >= 8 ? 22 : 14;
                matchType = "path";
            }
            else
            {
                continue;
            }

            if (points <= bestPoints)
            {
                continue;
            }

            bestPoints = points;
            bestReason = $"{(strong ? "strong" : "weak")} ship alias '{alias}' in {matchType}";
        }

        return (bestPoints, bestReason);
    }

    private static IReadOnlyCollection<string> SplitCompactSegments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => Path.GetFileNameWithoutExtension(segment))
            .Select(ShipAssetText.Compact)
            .Where(segment => segment.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static (int Points, string Reason) GetRoleBonus(
        string path,
        string extension,
        string role) => role switch
    {
        "ShipModel" when extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) =>
            (25, "OBJ model"),

        "ShipTexture" when ShipAssetText.IsImage(extension)
            && IsGenericShipTexture(path) =>
            (30, "generic ship texture"),

        "ShipTexture" when ShipAssetText.IsImage(extension)
            && path.Contains("/textures/", StringComparison.OrdinalIgnoreCase) =>
            (18, "ship texture folder"),

        "BaseToken" when ShipAssetText.IsImage(extension)
            && (path.Contains("token", StringComparison.OrdinalIgnoreCase)
                || path.Contains("base", StringComparison.OrdinalIgnoreCase)) =>
            (28, "token/base image"),

        "DialTexture" when ShipAssetText.IsImage(extension)
            && (path.Contains("dial", StringComparison.OrdinalIgnoreCase)
                || path.Contains("maneuver", StringComparison.OrdinalIgnoreCase)) =>
            (30, "dial image"),

        "DialModel" when extension.Equals(".obj", StringComparison.OrdinalIgnoreCase)
            && path.Contains("dial", StringComparison.OrdinalIgnoreCase) =>
            (30, "dial model"),

        "ShipScript" when extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) =>
            (20, "Lua script"),

        _ => (0, string.Empty)
    };

    private static bool IsGenericShipTexture(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.Equals("standard", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("default", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("rebel", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("scum", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("republic", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("separatist", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("resistance", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("firstorder", StringComparison.OrdinalIgnoreCase);
    }
}
