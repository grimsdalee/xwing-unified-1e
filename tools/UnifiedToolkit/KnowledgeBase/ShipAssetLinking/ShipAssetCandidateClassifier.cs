using UnifiedToolkit.KnowledgeBase;
namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetCandidateClassifier
{
    public string Classify(IReadOnlyList<KnowledgeBaseShipAssetCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "missing";
        }

        var hasClearLead = candidates.Count == 1
            || candidates[0].Score - candidates[1].Score >= 12;

        return candidates[0].Score >= 85 && hasClearLead
            ? "clear"
            : "review";
    }
}
