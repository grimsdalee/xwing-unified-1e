using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetEligibilityFilter
{
    private readonly LegacyAssetContextMatcher contextMatcher;

    public ShipAssetEligibilityFilter(LegacyAssetContextMatcher contextMatcher)
    {
        this.contextMatcher = contextMatcher;
    }

    public bool IsEligible(
        KnowledgeBaseAsset asset,
        string role,
        IReadOnlyList<LegacyAssetContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return role switch
        {
            "ShipModel" => IsObj(asset),
            "DialModel" => IsEligibleDialModel(asset, contexts),
            "ShipTexture" => IsEligibleShipTexture(asset, contexts),
            "BaseToken" => IsEligibleBaseToken(asset, contexts),
            "DialTexture" => IsEligibleDialTexture(asset, contexts),
            "ShipScript" => asset.Extension.Equals(".lua", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsObj(KnowledgeBaseAsset asset) =>
        asset.Extension.Equals(".obj", StringComparison.OrdinalIgnoreCase);

    private bool IsEligibleDialModel(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts)
    {
        if (!IsObj(asset))
        {
            return false;
        }

        var path = ShipAssetText.Normalize(asset.RepositoryPath);
        var fileName = Path.GetFileNameWithoutExtension(path);

        return path.Contains("/dial/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/dials/", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("dial", StringComparison.OrdinalIgnoreCase)
            || contextMatcher.IsPlausibleForRole(contexts, "DialModel");
    }

    private bool IsEligibleShipTexture(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts)
    {
        if (!ShipAssetText.IsImage(asset.Extension))
        {
            return false;
        }

        if (asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase))
        {
            return contextMatcher.IsPlausibleForRole(contexts, "ShipTexture");
        }

        var path = ShipAssetText.Normalize(asset.RepositoryPath);
        var fileName = Path.GetFileNameWithoutExtension(path);

        if (fileName.Equals("icon", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("blank", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("icon", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Contains("/textures/", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("texture", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("standard", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("default", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsEligibleBaseToken(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts)
    {
        if (!ShipAssetText.IsImage(asset.Extension))
        {
            return false;
        }

        var path = ShipAssetText.Normalize(asset.RepositoryPath);
        return path.Contains("token", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/base/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/bases/", StringComparison.OrdinalIgnoreCase)
            || contextMatcher.IsPlausibleForRole(contexts, "BaseToken");
    }

    private bool IsEligibleDialTexture(
        KnowledgeBaseAsset asset,
        IReadOnlyList<LegacyAssetContext> contexts)
    {
        if (!ShipAssetText.IsImage(asset.Extension))
        {
            return false;
        }

        var path = ShipAssetText.Normalize(asset.RepositoryPath);
        return path.Contains("dial", StringComparison.OrdinalIgnoreCase)
            || path.Contains("maneuver", StringComparison.OrdinalIgnoreCase)
            || contextMatcher.IsPlausibleForRole(contexts, "DialTexture");
    }
}
