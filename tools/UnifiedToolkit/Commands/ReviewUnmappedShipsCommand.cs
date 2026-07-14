using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ReviewUnmappedShipsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        var repoFolder = Path.GetFullPath(args[0]);
        var mappingFolder = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var entries = ShipDispositionReviewBuilder.Build(repository.Ships, mappings);
            var output = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var csv = Path.Combine(output, "unmapped-ship-review.csv");
            var json = Path.Combine(output, "ship-dispositions.review.json");
            ShipDispositionReviewReport.WriteCsv(entries, csv);
            ShipDispositionReviewReport.WriteJson(entries, json);

            Console.WriteLine("UnifiedToolkit Unmapped Ship Review");
            Console.WriteLine("====================================");
            Console.WriteLine();
            Console.WriteLine($"Mapping version:    {mappings.Version}");
            Console.WriteLine($"Ships for review:   {entries.Count}");
            Console.WriteLine($"Already reviewed:   {entries.Count(x => x.Kind != ShipDispositionKind.Unreviewed)}");
            Console.WriteLine($"CSV review:         {csv}");
            Console.WriteLine($"Editable JSON:      {json}");
            Console.WriteLine();
            Console.WriteLine("Set each JSON entry to Custom, Adapted, Alias, Excluded, or Deferred, and provide a reason.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Review generation failed: {ex.Message}"); return 1; }
    }

    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit review-unmapped-ships <repo-folder> [mapping-folder]");
}
