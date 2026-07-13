using UnifiedToolkit.Conversion;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ConvertCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);
        var allowSourceErrors = args.Any(x =>
            x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));
        var mappingArgument = args
            .Skip(1)
            .FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
        var mappingFolder = mappingArgument is null
            ? Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition")
            : Path.GetFullPath(mappingArgument);

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
            Console.WriteLine("UnifiedToolkit First Edition Conversion");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Repo folder:    {repoFolder}");
            Console.WriteLine($"Mapping folder: {mappingFolder}");
            Console.WriteLine();

            var source = RepositoryLoader.Load(repoFolder);
            var sourceIssues = RepositoryValidator.Validate(source);
            var sourceErrors = sourceIssues.Count(x =>
                x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"Source ships:              {source.Ships.Count}");
            Console.WriteLine($"Source validation errors:  {sourceErrors}");

            if (sourceErrors > 0 && !allowSourceErrors)
            {
                Console.WriteLine();
                Console.WriteLine("Conversion stopped because the source repository contains validation errors.");
                Console.WriteLine("Fix the errors, or use --allow-source-errors for diagnostic conversion.");
                return 2;
            }

            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var profile = new ConversionProfile
            {
                AllowSourceValidationErrors = allowSourceErrors
            };
            var result = ConversionEngine.ConvertShips(source, mappings, profile);

            var reportsFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var issuesPath = Path.Combine(reportsFolder, "conversion-issues.csv");
            var shipsPath = Path.Combine(reportsFolder, "first-edition-ships.csv");
            var coveragePath = Path.Combine(reportsFolder, "ship-mapping-coverage.csv");

            ConversionIssuesReport.Write(result.Issues, issuesPath);
            FirstEditionShipsReport.Write(result.Repository.Ships, shipsPath);
            ShipMappingCoverageReport.Write(result.ShipCoverage, coveragePath);

            var errors = result.Issues.Count(x =>
                x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            var warnings = result.Issues.Count(x =>
                x.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
            var information = result.Issues.Count(x =>
                x.Severity.Equals("Information", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"Mapping version:           {mappings.Version}");
            Console.WriteLine($"Converted ships:           {result.Repository.Ships.Count}");
            Console.WriteLine($"Excluded ships:            {result.ExcludedShipCount}");
            Console.WriteLine($"Unmapped ships:            {result.UnmappedShipCount}");
            Console.WriteLine($"Conversion errors:         {errors}");
            Console.WriteLine($"Conversion warnings:       {warnings}");
            Console.WriteLine($"Conversion information:    {information}");
            Console.WriteLine($"Issues report:             {issuesPath}");
            Console.WriteLine($"Ships report:              {shipsPath}");
            Console.WriteLine($"Coverage report:           {coveragePath}");

            return errors > 0 ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Conversion failed: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnifiedToolkit convert <repo-folder> [mapping-folder] [--allow-source-errors]");
    }
}
