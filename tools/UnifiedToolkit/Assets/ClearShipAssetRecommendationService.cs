namespace UnifiedToolkit.Assets;

public sealed class ClearShipAssetRecommendationResult
{
    public ShipAssetReviewDocument Document { get; init; } = new();
    public int ApprovedRecommendations { get; init; }
    public int RemainingUnreviewed { get; init; }
}

public static class ClearShipAssetRecommendationService
{
    public static ClearShipAssetRecommendationResult Build(ShipAssetReviewDocument source)
    {
        var approved = 0;
        var remaining = 0;
        var entries = new List<ShipAssetReviewEntry>(source.Entries.Count);

        foreach (var entry in source.Entries)
        {
            if (entry.Decision.Equals("Unreviewed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(entry.RecommendedAssetId))
            {
                approved++;
                entries.Add(Clone(
                    entry,
                    decision: "Approve",
                    selectedAssetId: entry.RecommendedAssetId,
                    reason: "Automatically accepted because the structurally compatible top candidate was clearly separated from the runner-up."));
                continue;
            }

            if (entry.Decision.Equals("Unreviewed", StringComparison.OrdinalIgnoreCase))
                remaining++;

            entries.Add(Clone(entry, entry.Decision, entry.SelectedAssetId, entry.Reason));
        }

        return new ClearShipAssetRecommendationResult
        {
            ApprovedRecommendations = approved,
            RemainingUnreviewed = remaining,
            Document = new ShipAssetReviewDocument
            {
                SchemaVersion = source.SchemaVersion,
                SourceAssetMappingVersion = source.SourceAssetMappingVersion,
                GeneratedUtc = DateTimeOffset.UtcNow,
                Entries = entries
            }
        };
    }

    private static ShipAssetReviewEntry Clone(
        ShipAssetReviewEntry source,
        string decision,
        string selectedAssetId,
        string reason) => new()
    {
        Entity = source.Entity,
        EntityName = source.EntityName,
        Role = source.Role,
        Decision = decision,
        SelectedAssetId = selectedAssetId,
        Reason = reason,
        RecommendedAssetId = source.RecommendedAssetId,
        RecommendedScore = source.RecommendedScore,
        RunnerUpScore = source.RunnerUpScore,
        Candidates = source.Candidates
    };
}
