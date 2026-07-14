using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ResolveOfficialShipAliasesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { ShowUsage(); return 1; }
        var repoFolder = Path.GetFullPath(args[0]);
        var firstEditionDataFolder = Path.GetFullPath(args[1]);
        var mappingFolder = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");

        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var catalogue = FirstEditionDataLoader.LoadShipCatalogue(firstEditionDataFolder);
            var officialShips = catalogue.Ships;
            var candidates = DeferredOfficialAliasResolver.Resolve(repository.Ships, officialShips, mappings);
            var reportsFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var reportPath = OfficialAliasCandidatesReport.Write(reportsFolder, candidates);
            var proposalsPath = OfficialAliasProposalsWriter.Write(reportsFolder, candidates);
            var sourcesReportPath = FirstEditionDataSourcesReport.Write(reportsFolder, catalogue.SourceFiles);

            Console.WriteLine("UnifiedToolkit Official Ship Alias Resolution");
            Console.WriteLine("=============================================");
            Console.WriteLine();
            Console.WriteLine($"Mapping version:       {mappings.Version}");
            Console.WriteLine($"Deferred ships:        {mappings.ShipDispositions.Count(x => x.Kind == ShipDispositionKind.Deferred)}");
            Console.WriteLine($"Proposed aliases:      {candidates.Count(x => x.Decision == "ProposedAlias")}");
            Console.WriteLine($"Require review:        {candidates.Count(x => x.Decision.StartsWith("Review", StringComparison.Ordinal))}");
            Console.WriteLine($"Epic model required:   {candidates.Count(x => x.Decision == "RequiresEpicCompositeModel")}");
            Console.WriteLine($"Remain deferred:       {candidates.Count(x => x.Decision == "RemainDeferred")}");
            Console.WriteLine($"Candidates report:     {reportPath}");
            Console.WriteLine($"Proposed mappings:     {proposalsPath}");
            Console.WriteLine($"Data sources report:   {sourcesReportPath}");
            Console.WriteLine();
            Console.WriteLine("No live mappings or dispositions were changed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Official alias resolution failed: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit resolve-official-ship-aliases <repo-folder> <xwing-data-folder> [mapping-folder]");
}
