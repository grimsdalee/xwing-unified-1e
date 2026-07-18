using UnifiedToolkit.KnowledgeBase;
namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed record ShipAssetRoleDefinition(string Name, bool Required);

public sealed class ShipAssetRoleCatalogue
{
    private static readonly IReadOnlyList<ShipAssetRoleDefinition> Definitions =
    [
        new("ShipModel", true),
        new("ShipTexture", true),
        new("BaseToken", true),
        new("DialTexture", true),
        new("DialModel", false),
        new("ShipScript", false)
    ];

    public IReadOnlyList<ShipAssetRoleDefinition> GetAll() => Definitions;
}
