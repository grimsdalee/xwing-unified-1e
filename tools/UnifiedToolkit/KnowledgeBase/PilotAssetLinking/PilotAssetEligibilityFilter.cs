using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotAssetEligibilityFilter
{
    public bool IsEligible(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts,
        string role,
        PilotIdentityProfile identity)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(identity);

        if (!IsImage(asset.Extension))
        {
            return false;
        }

        var path = asset.RepositoryPath.Replace('\\', '/').ToLowerInvariant();
        var compactPath = PilotIdentityProfile.Compact(path);
        var compactContext = PilotIdentityProfile.Compact(string.Join(" ", contexts.Select(context => context.SearchText)));
        var hasStrongIdentity = identity.StrongAliases.Any(alias =>
            compactPath.Contains(alias, StringComparison.OrdinalIgnoreCase)
            || compactContext.Contains(alias, StringComparison.OrdinalIgnoreCase));

        if (!hasStrongIdentity)
        {
            return false;
        }

        return role switch
        {
            "PilotCard" => IsPilotCard(asset, contexts, path),
            "PilotBaseTokenSheet" => IsPilotBaseTokenSheet(asset, contexts, path),
            "PilotBaseToken" => IsExtractedPilotBaseToken(asset, contexts, path),
            _ => false
        };
    }

    private static bool IsPilotCard(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts,
        string path)
    {
        if (asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase))
        {
            return contexts.Any(context =>
                context.PropertyName.Equals("FaceURL", StringComparison.OrdinalIgnoreCase)
                && context.JsonPath.Contains("CustomDeck", StringComparison.OrdinalIgnoreCase));
        }

        return ContainsAny(path, "/cards/", "/pilotcards/", "/pilots/")
               && !ContainsAny(path, "/textures/", "/tokens/", "dial", "condition");
    }

    private static bool IsPilotBaseTokenSheet(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts,
        string path)
    {
        if (asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase))
        {
            return contexts.Any(context =>
                context.PropertyName.Equals("DiffuseURL", StringComparison.OrdinalIgnoreCase)
                && context.JsonPath.Contains("CustomMesh", StringComparison.OrdinalIgnoreCase));
        }

        return ContainsAny(path, "token-sheet", "tokensheet", "token_atlas", "tokenatlas", "/base-tokens/")
               && !ContainsAny(path, "condition", "targetlock", "target-lock", "objective", "dial");
    }

    private static bool IsExtractedPilotBaseToken(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts,
        string path)
    {
        if (asset.IsGenerated)
        {
            return ContainsAny(path, "/pilot-base-tokens/", "/extracted-tokens/", "pilotbasetoken");
        }

        if (!asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(path, "/pilot-base-tokens/", "/extracted-tokens/", "pilotbasetoken")
                   && !ContainsAny(path, "sheet", "atlas", "condition", "targetlock", "target-lock", "objective", "dial");
        }

        return contexts.Any(context =>
            context.PropertyName.Equals("ImageURL", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(context.SearchText.ToLowerInvariant(), "pilot base token", "pilot token", "ship token"));
    }

    private static bool IsImage(string extension) =>
        extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
