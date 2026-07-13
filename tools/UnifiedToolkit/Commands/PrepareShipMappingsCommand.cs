using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Candidates;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class PrepareShipMappingsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);
        var mappingFolder = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");

        if (!Directory.Exists(repoFolder))
        {
            Console.Error.WriteLine($"Repo folder not found: {repoFolder}");
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
            var candidates = ShipMappingCandidateBuilder.Build(repository.Ships, mappings);

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports",
                "conversion");

            var reportPath = ShipMappingCandidatesReport.Write(reportsFolder, candidates);

            var mapped = candidates.Count(x => x.Decision == "Mapped");
            var excluded = candidates.Count(x => x.Decision == "Excluded");
            var review = candidates.Count(x => x.Decision == "Review");
            var collisions = candidates.Count(x => x.Decision == "ReviewCollision");

            Console.WriteLine("UnifiedToolkit Ship Mapping Preparation");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Repo folder:       {repoFolder}");
            Console.WriteLine($"Mapping folder:    {mappingFolder}");
            Console.WriteLine($"Mapping version:   {mappings.Version}");
            Console.WriteLine();
            Console.WriteLine($"Source ships:      {candidates.Count}");
            Console.WriteLine($"Already mapped:    {mapped}");
            Console.WriteLine($"Already excluded:  {excluded}");
            Console.WriteLine($"Require review:    {review}");
            Console.WriteLine($"ID collisions:     {collisions}");
            Console.WriteLine($"Candidates report: {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Name-derived candidates are identity suggestions only.");
            Console.WriteLine("They do not confirm official First Edition availability or card statistics.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ship mapping preparation failed: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnifiedToolkit prepare-ship-mappings <repo-folder> [mapping-folder]");
    }
}
