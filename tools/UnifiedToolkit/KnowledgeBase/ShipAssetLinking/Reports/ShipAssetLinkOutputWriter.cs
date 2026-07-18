using UnifiedToolkit.KnowledgeBase;
namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking.Reports;

public sealed class ShipAssetLinkOutputWriter
{
    private readonly ShipAssetLinkReviewCsvWriter csvWriter;
    private readonly ShipAssetLinkSummaryWriter summaryWriter;

    public ShipAssetLinkOutputWriter(
        ShipAssetLinkReviewCsvWriter csvWriter,
        ShipAssetLinkSummaryWriter summaryWriter)
    {
        this.csvWriter = csvWriter;
        this.summaryWriter = summaryWriter;
    }

    public static ShipAssetLinkOutputWriter CreateDefault() => new(
        new ShipAssetLinkReviewCsvWriter(),
        new ShipAssetLinkSummaryWriter());

    public void Write(
        string outputRoot,
        UnifiedKnowledgeBase knowledgeBase,
        IReadOnlyCollection<KnowledgeBaseShip> linkedShips)
    {
        var reportsRoot = Path.Combine(outputRoot, "reports");
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(reportsRoot);

        ShipAssetJson.Write(Path.Combine(outputRoot, "knowledge-base.json"), knowledgeBase);
        ShipAssetJson.Write(Path.Combine(outputRoot, "ship-links.json"), new KnowledgeBaseShipDomain
        {
            SchemaVersion = "1.1.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            Ships = linkedShips.ToList()
        });

        csvWriter.Write(Path.Combine(reportsRoot, "ship-link-review.csv"), linkedShips);
        summaryWriter.Write(Path.Combine(reportsRoot, "SHIP-LINK-SUMMARY.md"), linkedShips);
    }
}
