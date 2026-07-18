namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetCandidateEvidencePolicy
{
    private static readonly string[] NonShipTokenTerms =
    {
        "/condition/",
        "/conditions/",
        "condition-token",
        "conditiontoken",
        "roll-token",
        "rolltoken",
        "target-lock",
        "targetlock",
        "objective-token",
        "objectivetoken",
        "focus-token",
        "evade-token",
        "stress-token",
        "shield-token",
        "ion-token",
        "tractor-token",
        "jam-token",
        "reinforce-token",
        "cloak-token",
        "charge-token",
        "force-token"
    };

    public bool HasRequiredEvidence(
        string role,
        KnowledgeBaseShipAssetCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(candidate);

        return role switch
        {
            "BaseToken" => HasPilotShipTokenEvidence(candidate),
            "DialTexture" => HasDialEvidence(candidate),
            _ => true
        };
    }

    private static bool HasPilotShipTokenEvidence(KnowledgeBaseShipAssetCandidate candidate)
    {
        var normalizedPath = candidate.RepositoryPath.Replace('\\', '/');
        if (NonShipTokenTerms.Any(term => normalizedPath.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (candidate.Reasons.Any(reason =>
                reason.Contains("pilot ship-token context", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var hasStrongShipIdentity = candidate.Reasons.Any(reason =>
            reason.Contains("strong ship alias", StringComparison.OrdinalIgnoreCase));

        var hasExplicitBaseTokenPath = ContainsAny(
            normalizedPath,
            "ship-token",
            "shiptoken",
            "pilot-token",
            "pilottoken",
            "/base-token/",
            "/basetoken/");

        return hasStrongShipIdentity && hasExplicitBaseTokenPath;
    }

    private static bool HasDialEvidence(KnowledgeBaseShipAssetCandidate candidate) =>
        candidate.Reasons.Any(reason =>
            reason.Contains("strong ship alias", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("legacy object context", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("shared epic dial context", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
