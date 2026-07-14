using UnifiedToolkit.Conversion;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ConvertCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("Usage: UnifiedToolkit convert <repo-folder> [mapping-folder] [--allow-source-errors]"); return 1; }
        var repoFolder = Path.GetFullPath(args[0]);
        var allowSourceErrors = args.Any(x => x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));
        var mappingArgument = args.Skip(1).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
        var mappingFolder = mappingArgument is null ? Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition") : Path.GetFullPath(mappingArgument);
        try
        {
            Console.WriteLine("UnifiedToolkit First Edition Conversion"); Console.WriteLine("======================================="); Console.WriteLine();
            Console.WriteLine($"Repo folder:    {repoFolder}"); Console.WriteLine($"Mapping folder: {mappingFolder}"); Console.WriteLine();
            var source = RepositoryLoader.Load(repoFolder);
            var sourceErrors = RepositoryValidator.Validate(source).Count(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"Source ships:              {source.Ships.Count}");
            Console.WriteLine($"Source pilots:             {source.Pilots.Count}");
            Console.WriteLine($"Source upgrades:           {source.Upgrades.Count}");
            Console.WriteLine($"Source validation errors:  {sourceErrors}");
            if (sourceErrors > 0 && !allowSourceErrors) { Console.WriteLine("Conversion stopped because the source repository contains validation errors."); return 2; }
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var result = ConversionEngine.ConvertRepository(source, mappings, new ConversionProfile { AllowSourceValidationErrors = allowSourceErrors });
            var folder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var issuesPath = Path.Combine(folder, "conversion-issues.csv");
            var shipsPath = Path.Combine(folder, "first-edition-ships.csv");
            var shipCoveragePath = Path.Combine(folder, "ship-mapping-coverage.csv");
            var pilotsPath = Path.Combine(folder, "first-edition-pilots.csv");
            var pilotCoveragePath = Path.Combine(folder, "pilot-mapping-coverage.csv");
            var upgradesPath = Path.Combine(folder, "first-edition-upgrades.csv");
            var upgradeCoveragePath = Path.Combine(folder, "upgrade-mapping-coverage.csv");
            ConversionIssuesReport.Write(result.Issues, issuesPath);
            FirstEditionShipsReport.Write(result.Repository.Ships, shipsPath);
            ShipMappingCoverageReport.Write(result.ShipCoverage, shipCoveragePath);
            FirstEditionPilotsReport.Write(result.Repository.Pilots, pilotsPath);
            PilotMappingCoverageReport.Write(result.PilotCoverage, pilotCoveragePath);
            FirstEditionUpgradesReport.Write(result.Repository.Upgrades, upgradesPath);
            UpgradeMappingCoverageReport.Write(result.UpgradeCoverage, upgradeCoveragePath);
            var errors = result.Issues.Count(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            var warnings = result.Issues.Count(x => x.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
            var information = result.Issues.Count(x => x.Severity.Equals("Information", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"Mapping version:           {mappings.Version}");
            Console.WriteLine($"Converted ships:           {result.Repository.Ships.Count}");
            Console.WriteLine($"Deferred/planned ships:    {result.DeferredShipCount}");
            Console.WriteLine($"Converted pilots:          {result.Repository.Pilots.Count}");
            Console.WriteLine($"Alternate pilot printings: {result.PilotCoverage.Count(x => x.Status == "AlternatePrinting")}");
            Console.WriteLine($"Ambiguous pilots:          {result.PilotCoverage.Count(x => x.Status == "Ambiguous")}");
            Console.WriteLine($"Deferred-ship pilots:      {result.PilotCoverage.Count(x => x.Status == "SourceShipDeferred")}");
            Console.WriteLine($"Pilots not official:       {result.PilotCoverage.Count(x => x.Status == "NotInOfficialDataset")}");
            Console.WriteLine($"Converted upgrades:        {result.Repository.Upgrades.Count}");
            Console.WriteLine($"Alternate upgrade prints:  {result.UpgradeCoverage.Count(x => x.Status == "AlternatePrinting")}");
            Console.WriteLine($"Ambiguous upgrades:        {result.UpgradeCoverage.Count(x => x.Status == "Ambiguous")}");
            Console.WriteLine($"Upgrades not official:     {result.UpgradeCoverage.Count(x => x.Status == "NotInOfficialDataset")}");
            Console.WriteLine($"Unmapped upgrades:         {result.UpgradeCoverage.Count(x => x.Status == "Unmapped")}");
            Console.WriteLine($"Conversion errors:         {errors}");
            Console.WriteLine($"Conversion warnings:       {warnings}");
            Console.WriteLine($"Conversion information:    {information}");
            Console.WriteLine($"Issues report:             {issuesPath}");
            Console.WriteLine($"Ships report:              {shipsPath}");
            Console.WriteLine($"Ship coverage report:      {shipCoveragePath}");
            Console.WriteLine($"Pilots report:             {pilotsPath}");
            Console.WriteLine($"Pilot coverage report:     {pilotCoveragePath}");
            Console.WriteLine($"Upgrades report:           {upgradesPath}");
            Console.WriteLine($"Upgrade coverage report:   {upgradeCoveragePath}");
            return errors > 0 ? 2 : 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Conversion failed: {ex.Message}"); return 1; }
    }
}
