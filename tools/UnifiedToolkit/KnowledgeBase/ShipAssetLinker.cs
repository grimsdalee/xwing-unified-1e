using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase;

public sealed class ShipAssetLinker
{
    private readonly ShipAssetLinkingService service;

    public ShipAssetLinker()
        : this(ShipAssetLinkingService.CreateDefault())
    {
    }

    internal ShipAssetLinker(ShipAssetLinkingService service)
    {
        this.service = service;
    }

    public ShipAssetLinkResult Link(
        string repositoryRoot,
        string? shipsFile = null,
        string? outputFolder = null,
        int candidatesPerRole = 8)
    {
        var options = ShipAssetLinkingOptions.Create(
            repositoryRoot,
            shipsFile,
            outputFolder,
            candidatesPerRole);

        return service.Link(options);
    }
}
