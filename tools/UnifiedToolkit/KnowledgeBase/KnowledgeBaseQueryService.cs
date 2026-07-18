using System.Text.Json;

namespace UnifiedToolkit.KnowledgeBase;

public sealed class KnowledgeBaseQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UnifiedKnowledgeBase Load(string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var path = Path.Combine(repositoryRoot, "ukb", "knowledge-base.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Knowledge base not found. Run build-knowledge-base first.",
                path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UnifiedKnowledgeBase>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize knowledge base: {path}");
    }

    public IReadOnlyList<KnowledgeBaseAsset> SearchAssets(UnifiedKnowledgeBase ukb, string term)
    {
        term = term.Trim();
        if (term.Length == 0)
        {
            return Array.Empty<KnowledgeBaseAsset>();
        }

        return ukb.Domains.Assets
            .Where(asset => Contains(asset.AssetId, term)
                || Contains(asset.Sha256, term)
                || Contains(asset.RepositoryPath, term)
                || Contains(asset.FileName, term)
                || Contains(asset.Extension, term)
                || Contains(asset.AssetType, term)
                || Contains(asset.Warehouse, term)
                || asset.SourceReferences.Any(source => Contains(source.SourceLocation, term)))
            .OrderBy(asset => Rank(asset, term))
            .ThenBy(asset => asset.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public KnowledgeBaseAsset? FindAsset(UnifiedKnowledgeBase ukb, string target)
    {
        target = target.Trim();
        return ukb.Domains.Assets.FirstOrDefault(asset =>
                   asset.AssetId.Equals(target, StringComparison.OrdinalIgnoreCase))
               ?? ukb.Domains.Assets.FirstOrDefault(asset =>
                   asset.Sha256.Equals(target, StringComparison.OrdinalIgnoreCase))
               ?? ukb.Domains.Assets.FirstOrDefault(asset =>
                   asset.RepositoryPath.Equals(Normalize(target), StringComparison.OrdinalIgnoreCase))
               ?? ukb.Domains.Assets.FirstOrDefault(asset =>
                   asset.FileName.Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<KnowledgeBaseUnavailableSource> SearchUnavailable(UnifiedKnowledgeBase ukb, string? term = null)
    {
        IEnumerable<KnowledgeBaseUnavailableSource> query = ukb.Domains.UnavailableSources;
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(source => Contains(source.SourceId, term)
                || Contains(source.SourceUrl, term)
                || Contains(source.Host, term)
                || Contains(source.AssetType, term)
                || Contains(source.Status, term)
                || Contains(source.Reason, term));
        }

        return query
            .OrderBy(source => source.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int Rank(KnowledgeBaseAsset asset, string term)
    {
        if (asset.AssetId.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (asset.FileName.Equals(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (asset.RepositoryPath.Equals(Normalize(term), StringComparison.OrdinalIgnoreCase)) return 2;
        if (asset.AssetId.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 3;
        if (asset.FileName.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 4;
        return 10;
    }

    private static bool Contains(string? value, string term)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
