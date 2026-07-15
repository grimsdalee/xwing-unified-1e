using System.Text;
using System.Text.Json;
using UnifiedToolkit.Assets;

namespace UnifiedToolkit.Reports;

public static class AssetCatalogueReports
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteCatalogue(AssetCatalogue catalogue, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(catalogue, JsonOptions), Encoding.UTF8);
    }

    public static void WriteMatches(IEnumerable<AssetMatchCandidate> matches, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("EntityType,EntityId,EntityName,EntityShipId,EntityFaction,EntitySlot,SemanticKey,Role,AssetId,AssetName,AssetKind,StructuralClass,SourceKind,Location,MatchMethod,Confidence,RoleScore,ContextScore,Score,ConfidenceBand,Recommended,Notes");
        foreach (var item in matches)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(item.EntityType), Csv(item.EntityId), Csv(item.EntityName), Csv(item.EntityShipId), Csv(item.EntityFaction),
                Csv(item.EntitySlot), Csv(item.SemanticKey), Csv(item.Role.ToString()), Csv(item.AssetId), Csv(item.AssetName),
                Csv(item.AssetKind.ToString()), Csv(item.StructuralClass.ToString()), Csv(item.SourceKind.ToString()), Csv(item.Location), Csv(item.MatchMethod),
                Decimal(item.Confidence), Decimal(item.RoleScore), Decimal(item.ContextScore), Decimal(item.Score), Csv(item.ConfidenceBand.ToString()),
                item.Recommended ? "True" : "False", Csv(item.Notes)
            }));
        }
    }

    public static void WriteRoleCoverage(IEnumerable<AssetRoleRequirement> requirements, IEnumerable<AssetMatchCandidate> matches, string path)
    {
        var grouped = matches
            .Where(x => x.ConfidenceBand != AssetConfidenceBand.Rejected)
            .GroupBy(x => (x.SemanticKey, x.Role))
            .ToDictionary(x => x.Key, x => x.ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("EntityType,EntityId,EntityName,ShipId,Faction,Slot,SemanticKey,Role,Required,CandidateCount,RecommendedAssetId,RecommendedScore,ConfidenceBand,Status");
        foreach (var req in requirements.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role))
        {
            grouped.TryGetValue((req.Entity.SemanticKey, req.Role), out var found);
            found ??= Array.Empty<AssetMatchCandidate>();
            var best = found.OrderByDescending(x => x.Score).FirstOrDefault();
            var status = !req.Required && best is null ? "NotRequired" : best is null ? "Missing" : best.ConfidenceBand.ToString();
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(req.Entity.EntityType), Csv(req.Entity.EntityId), Csv(req.EntityName), Csv(req.Entity.ShipId), Csv(req.Entity.Faction),
                Csv(req.Entity.Slot), Csv(req.Entity.SemanticKey), Csv(req.Role.ToString()), req.Required ? "True" : "False",
                found.Length.ToString(), Csv(best?.AssetId), best is null ? "" : Decimal(best.Score), Csv(best?.ConfidenceBand.ToString()), Csv(status)
            }));
        }
    }

    public static void WriteResolutionSummary(IEnumerable<AssetResolutionReview> reviews, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("EntityType,EntityId,EntityName,ShipId,Faction,Slot,SemanticKey,Role,Required,Decision,CandidateCount,TopAssetId,TopAssetName,TopStructuralClass,TopScore,TopConfidenceBand");
        foreach (var review in reviews.OrderBy(x => x.Entity.SemanticKey).ThenBy(x => x.Role))
        {
            var top = review.Candidates.FirstOrDefault();
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(review.Entity.EntityType), Csv(review.Entity.EntityId), Csv(review.EntityName), Csv(review.Entity.ShipId),
                Csv(review.Entity.Faction), Csv(review.Entity.Slot), Csv(review.Entity.SemanticKey), Csv(review.Role.ToString()),
                review.Required ? "True" : "False", Csv(review.Decision), review.Candidates.Count.ToString(), Csv(top?.AssetId),
                Csv(top?.AssetName), Csv(top?.StructuralClass.ToString()), top is null ? "" : Decimal(top.Score), Csv(top?.ConfidenceBand.ToString())
            }));
        }
    }

    public static void WriteResolutionReview(IEnumerable<AssetResolutionReview> reviews, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(reviews, JsonOptions), Encoding.UTF8);
    }

    private static string Decimal(decimal value) => value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        value ??= "";
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
