using UnifiedToolkit.KnowledgeBase;
namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetReferenceUpdater
{
    public void ReplaceShipReferences(
        UnifiedKnowledgeBase knowledgeBase,
        IReadOnlyCollection<KnowledgeBaseShip> linkedShips)
    {
        knowledgeBase.Domains.Ships.Clear();
        knowledgeBase.Domains.Ships.AddRange(linkedShips);

        foreach (var asset in knowledgeBase.Domains.Assets)
        {
            asset.ReferencedBy.RemoveAll(reference =>
                reference.EntityType.Equals("ship", StringComparison.OrdinalIgnoreCase));
        }

        var assetsByKey = knowledgeBase.Domains.Assets.ToDictionary(
            asset => CreateKey(asset.AssetId, asset.RepositoryPath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var ship in linkedShips)
        {
            foreach (var role in ship.AssetRoles)
            {
                foreach (var candidate in role.Candidates)
                {
                    var key = CreateKey(candidate.AssetId, candidate.RepositoryPath);
                    if (!assetsByKey.TryGetValue(key, out var asset))
                    {
                        throw new InvalidDataException(
                            $"Candidate asset '{candidate.AssetId}' at '{candidate.RepositoryPath}' was not found in the knowledge base.");
                    }

                    asset.ReferencedBy.Add(new KnowledgeBaseEntityReference
                    {
                        EntityType = "ship",
                        EntityId = ship.ShipId,
                        Role = $"candidate:{role.Role}:{candidate.Score}"
                    });
                }
            }
        }
    }

    private static string CreateKey(string assetId, string repositoryPath) =>
        $"{assetId}\u001f{repositoryPath}";
}
