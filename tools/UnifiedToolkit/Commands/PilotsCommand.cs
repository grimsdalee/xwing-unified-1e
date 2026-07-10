using UnifiedToolkit.Reports;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class PilotsCommand
{
    private const int PreviewCount = 30;

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);

        if (!Directory.Exists(repoFolder))
        {
            Console.Error.WriteLine(
                $"Repo folder not found: {repoFolder}");

            return 1;
        }

        try
        {
            var ships = ShipParser.ParseFromRepo(repoFolder);
            var pilots = PilotParser.ParseFromRepo(repoFolder);

            PilotShipLinker.Link(pilots, ships);

            var validationIssues =
                PilotValidator.Validate(pilots);

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports");

            var pilotsReportPath = Path.Combine(
                reportsFolder,
                "pilots.csv");

            var unmatchedReportPath = Path.Combine(
                reportsFolder,
                "unmatched-pilots.csv");

            var validationReportPath = Path.Combine(
                reportsFolder,
                "pilot-validation.csv");

            PilotsReport.Write(
                pilots,
                pilotsReportPath);

            UnmatchedPilotsReport.Write(
                pilots,
                unmatchedReportPath);

            PilotValidationReport.Write(
                validationIssues,
                validationReportPath);

            PrintSummary(
                repoFolder,
                pilotsReportPath,
                unmatchedReportPath,
                validationReportPath,
                ships,
                pilots,
                validationIssues);

            return validationIssues.Any(
                issue => issue.Severity.Equals(
                    "Error",
                    StringComparison.OrdinalIgnoreCase))
                ? 2
                : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Unable to parse pilots: " +
                $"{exception.Message}");

            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  UnifiedToolkit pilots <repo-folder>");
    }

    private static void PrintSummary(
        string repoFolder,
        string pilotsReportPath,
        string unmatchedReportPath,
        string validationReportPath,
        IReadOnlyCollection<ShipDefinition> ships,
        IReadOnlyCollection<PilotDefinition> pilots,
        IReadOnlyCollection<PilotValidationIssue>
            validationIssues)
    {
        var linkedCount = pilots.Count(
            pilot => pilot.IsLinkedToShip);

        var unmatchedCount = pilots.Count - linkedCount;

        var errorCount = validationIssues.Count(
            issue => issue.Severity.Equals(
                "Error",
                StringComparison.OrdinalIgnoreCase));

        var warningCount = validationIssues.Count(
            issue => issue.Severity.Equals(
                "Warning",
                StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("UnifiedToolkit Pilots");
        Console.WriteLine("=====================");
        Console.WriteLine();

        Console.WriteLine(
            $"Repo folder:         {repoFolder}");

        Console.WriteLine(
            $"Ships available:     {ships.Count}");

        Console.WriteLine(
            $"Pilots found:        {pilots.Count}");

        Console.WriteLine(
            $"Pilots linked:       {linkedCount}");

        Console.WriteLine(
            $"Pilots unmatched:    {unmatchedCount}");

        Console.WriteLine(
            $"Validation issues:   {validationIssues.Count}");

        Console.WriteLine(
            $"Validation errors:   {errorCount}");

        Console.WriteLine(
            $"Validation warnings: {warningCount}");

        Console.WriteLine(
            $"Pilots report:       {pilotsReportPath}");

        Console.WriteLine(
            $"Unmatched report:    {unmatchedReportPath}");

        Console.WriteLine(
            $"Validation report:   {validationReportPath}");

        Console.WriteLine();

        foreach (var pilot in pilots.Take(PreviewCount))
        {
            Console.WriteLine(
                $"{pilot.Name} [{pilot.Id}]");

            Console.WriteLine(
                $"  Faction:    {pilot.Faction}");

            Console.WriteLine(
                $"  Ship type:  {pilot.ShipType}");

            Console.WriteLine(
                $"  Ship link:  " +
                $"{pilot.Ship?.Name ?? "(unmatched)"}");

            Console.WriteLine(
                $"  Initiative: {pilot.Initiative}");

            if (pilot.Actions.Count > 0)
            {
                Console.WriteLine(
                    $"  Actions:    " +
                    $"{string.Join(", ", pilot.Actions)}");
            }

            Console.WriteLine();
        }

        if (pilots.Count > PreviewCount)
        {
            Console.WriteLine(
                $"Showing first {PreviewCount} of " +
                $"{pilots.Count} pilots.");
        }

        if (validationIssues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Validation summary");
            Console.WriteLine("------------------");

            foreach (var group in validationIssues
                         .GroupBy(issue => new
                         {
                             issue.Severity,
                             issue.Code
                         })
                         .OrderBy(group =>
                             group.Key.Severity)
                         .ThenBy(group =>
                             group.Key.Code))
            {
                Console.WriteLine(
                    $"{group.Key.Severity}: " +
                    $"{group.Key.Code} " +
                    $"({group.Count()})");
            }
        }
    }
}