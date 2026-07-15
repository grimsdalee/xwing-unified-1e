namespace UnifiedToolkit.Assets;

public sealed class CuratedShipAssetReviewResult
{
    public string SourceAssetMappingVersion { get; init; } = "";
    public IReadOnlyDictionary<AssetRole, ShipAssetReviewDocument> Documents { get; init; }
        = new Dictionary<AssetRole, ShipAssetReviewDocument>();

    public int TotalEntries => Documents.Values.Sum(x => x.Entries.Count);
}

public static class CuratedShipAssetReviewService
{
    private static readonly AssetRole[] ReviewRoles =
    {
        AssetRole.ShipModel,
        AssetRole.ShipTexture,
        AssetRole.ShipBase,
        AssetRole.ShipDial
    };

    public static CuratedShipAssetReviewResult Build(string shipReviewPath)
    {
        var source = ShipAssetReviewService.LoadDocument(shipReviewPath);
        var documents = new Dictionary<AssetRole, ShipAssetReviewDocument>();

        foreach (var role in ReviewRoles)
        {
            var entries = source.Entries
                .Where(x => x.Role == role)
                .Select(CloneForCuratedReview)
                .OrderBy(x => x.EntityName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Entity.SemanticKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            documents[role] = new ShipAssetReviewDocument
            {
                SchemaVersion = "1.0",
                SourceAssetMappingVersion = source.SourceAssetMappingVersion,
                GeneratedUtc = DateTimeOffset.UtcNow,
                Entries = entries
            };
        }

        return new CuratedShipAssetReviewResult
        {
            SourceAssetMappingVersion = source.SourceAssetMappingVersion,
            Documents = documents
        };
    }

    public static string FileNameFor(AssetRole role) => role switch
    {
        AssetRole.ShipModel => "ship-models.review.json",
        AssetRole.ShipTexture => "ship-textures.review.json",
        AssetRole.ShipBase => "ship-bases.review.json",
        AssetRole.ShipDial => "ship-dials.review.json",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported curated ship role.")
    };

    private static ShipAssetReviewEntry CloneForCuratedReview(ShipAssetReviewEntry source) => new()
    {
        Entity = source.Entity,
        EntityName = source.EntityName,
        Role = source.Role,
        Decision = "Unreviewed",
        SelectedAssetId = "",
        Reason = "",
        RecommendedAssetId = "",
        RecommendedScore = source.RecommendedScore,
        RunnerUpScore = source.RunnerUpScore,
        Candidates = source.Candidates
    };
}
