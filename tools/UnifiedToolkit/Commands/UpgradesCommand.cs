using UnifiedToolkit.Reports;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class UpgradesCommand
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
            var upgrades =
                UpgradeParser.ParseFromRepo(repoFolder);

            var validationIssues =
                UpgradeValidator.Validate(upgrades);

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports");

            var reportPath = Path.Combine(
                reportsFolder,
                "upgrades.csv");

            var validationReportPath = Path.Combine(
                reportsFolder,
                "upgrade-validation.csv");

            UpgradesReport.Write(
                upgrades,
                reportPath);

            UpgradeValidationReport.Write(
                validationIssues,
                validationReportPath);

            PrintSummary(
                repoFolder,
                reportPath,
                validationReportPath,
                upgrades,
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
                $"Unable to parse upgrades: " +
                exception.Message);

            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  UnifiedToolkit upgrades <repo-folder>");
    }

    private static void PrintSummary(
    string repoFolder,
    string reportPath,
    string validationReportPath,
    IReadOnlyCollection<UpgradeDefinition> upgrades,
    IReadOnlyCollection<UpgradeValidationIssue>
        validationIssues)
    {
        var errorCount = validationIssues.Count(
            issue => issue.Severity.Equals(
                "Error",
                StringComparison.OrdinalIgnoreCase));

        var warningCount = validationIssues.Count(
            issue => issue.Severity.Equals(
                "Warning",
                StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("UnifiedToolkit Upgrades");
        Console.WriteLine("=======================");
        Console.WriteLine();

        Console.WriteLine(
            $"Repo folder:         {repoFolder}");

        Console.WriteLine(
            $"Upgrades found:      {upgrades.Count}");

        Console.WriteLine(
            $"Validation issues:   {validationIssues.Count}");

        Console.WriteLine(
            $"Validation errors:   {errorCount}");

        Console.WriteLine(
            $"Validation warnings: {warningCount}");

        Console.WriteLine(
            $"Upgrades report:     {reportPath}");

        Console.WriteLine(
            $"Validation report:   {validationReportPath}");

        Console.WriteLine();

        foreach (var upgrade in upgrades.Take(PreviewCount))
        {
            Console.WriteLine(
                $"{upgrade.Name} [{upgrade.Id}]");

            Console.WriteLine(
                $"  Slot:       {upgrade.Slot}");

            if (upgrade.Limited > 0)
            {
                Console.WriteLine(
                    $"  Limited:    {upgrade.Limited}");
            }

            if (upgrade.Charges > 0)
            {
                Console.WriteLine(
                    $"  Charges:    {upgrade.Charges}");
            }

            if (upgrade.AddedSlots.Count > 0)
            {
                Console.WriteLine(
                    $"  Add slots:  " +
                    string.Join(", ", upgrade.AddedSlots));
            }

            if (upgrade.RemovedSlots.Count > 0)
            {
                Console.WriteLine(
                    $"  Remove:     " +
                    string.Join(", ", upgrade.RemovedSlots));
            }

            Console.WriteLine();
        }

        if (upgrades.Count > PreviewCount)
        {
            Console.WriteLine(
                $"Showing first {PreviewCount} of " +
                $"{upgrades.Count} upgrades.");
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