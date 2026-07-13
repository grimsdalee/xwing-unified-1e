using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ImportFirstEditionShipsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);
        var firstEditionDataFolder = Path.GetFullPath(args[1]);
        var mappingFolder = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");

        if (!Directory.Exists(repoFolder))
        {
            Console.Error.WriteLine($"Repo folder not found: {repoFolder}");
            return 1;
        }

        if (!Directory.Exists(firstEditionDataFolder))
        {
            Console.Error.WriteLine($"First Edition data folder not found: {firstEditionDataFolder}");
            return 1;
        }

        if (!Directory.Exists(mappingFolder))
        {
            Console.Error.WriteLine($"Mapping folder not found: {mappingFolder}");
            return 1;
        }

        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var officialShips = FirstEditionDataLoader.LoadShips(firstEditionDataFolder);
            var matches = OfficialShipMatcher.Match(repository.Ships, officialShips, mappings);

            var reportsFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var reportPath = OfficialShipMatchesReport.Write(reportsFolder, matches);
            var proposedPath = ProposedShipMappingsWriter.Write(reportsFolder, mappings.Ships, matches);

            Console.WriteLine("UnifiedToolkit First Edition Ship Import");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine($"Unified repo:          {repoFolder}");
            Console.WriteLine($"First Edition data:    {firstEditionDataFolder}");
            Console.WriteLine($"Mapping folder:        {mappingFolder}");
            Console.WriteLine($"Official ships loaded: {officialShips.Count}");
            Console.WriteLine($"Unified ships:         {repository.Ships.Count}");
            Console.WriteLine();
            Console.WriteLine($"Already mapped:        {matches.Count(match => match.Decision == "AlreadyMapped")}");
            Console.WriteLine($"Proposed direct:       {matches.Count(match => match.Decision == "ProposedDirect")}");
            Console.WriteLine($"Require review:        {matches.Count(match => match.Decision.StartsWith("Review", StringComparison.Ordinal))}");
            Console.WriteLine($"Not in official data:  {matches.Count(match => match.Decision == "NotInOfficialDataset")}");
            Console.WriteLine($"Excluded:              {matches.Count(match => match.Decision == "Excluded")}");
            Console.WriteLine($"Match report:          {reportPath}");
            Console.WriteLine($"Proposed mappings:     {proposedPath}");
            Console.WriteLine();
            Console.WriteLine("ships.proposed.json is a review artifact and does not replace ships.json automatically.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition ship import failed: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnifiedToolkit import-first-edition-ships <repo-folder> <xwing-data-folder> [mapping-folder]");
    }
}
