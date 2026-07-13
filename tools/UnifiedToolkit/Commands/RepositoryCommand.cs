using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class RepositoryCommand
{
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
            Console.WriteLine("UnifiedToolkit Repository");
            Console.WriteLine("=========================");
            Console.WriteLine();

            Console.WriteLine(
                $"Repo folder: {repoFolder}");

            Console.WriteLine(
                "Loading repository...");

            Console.WriteLine();

            var repository =
                RepositoryLoader.Load(repoFolder);

            var linkedPilotCount =
                repository.Pilots.Count(
                    pilot => pilot.IsLinkedToShip);

            var unmatchedPilotCount =
                repository.Pilots.Count -
                linkedPilotCount;

            var validationIssues =
                RepositoryValidator.Validate(repository);

            var errorCount = validationIssues.Count(
                issue => issue.Severity.Equals(
                    "Error",
                    StringComparison.OrdinalIgnoreCase));

            var warningCount = validationIssues.Count(
                issue => issue.Severity.Equals(
                    "Warning",
                    StringComparison.OrdinalIgnoreCase));

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports");

            var validationReportPath = Path.Combine(
                reportsFolder,
                "repository-validation.csv");

            RepositoryValidationReport.Write(
                validationIssues,
                validationReportPath);

            Console.WriteLine(
                $"Ships:               " +
                $"{repository.Ships.Count}");

            Console.WriteLine(
                $"Pilots:              " +
                $"{repository.Pilots.Count}");

            Console.WriteLine(
                $"Upgrades:            " +
                $"{repository.Upgrades.Count}");

            Console.WriteLine(
                $"Pilots linked:       " +
                $"{linkedPilotCount}");

            Console.WriteLine(
                $"Pilots unmatched:    " +
                $"{unmatchedPilotCount}");

            Console.WriteLine(
                $"Validation issues:   " +
                $"{validationIssues.Count}");

            Console.WriteLine(
                $"Validation errors:   " +
                $"{errorCount}");

            Console.WriteLine(
                $"Validation warnings: " +
                $"{warningCount}");

            Console.WriteLine(
                $"Validation report:   " +
                $"{validationReportPath}");

            var samplePilot =
                repository.FindPilot("lukeskywalker");

            if (samplePilot is not null)
            {
                Console.WriteLine();
                Console.WriteLine("Lookup test");
                Console.WriteLine("-----------");

                Console.WriteLine(
                    $"Pilot:   {samplePilot.Name}");

                Console.WriteLine(
                    $"Ship:    " +
                    $"{samplePilot.Ship?.Name ?? "(unmatched)"}");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Lookup test: pilot " +
                    "'lukeskywalker' was not found.");
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
                                 issue.Category,
                                 issue.Code
                             })
                             .OrderBy(group =>
                                 SeverityOrder(
                                     group.Key.Severity))
                             .ThenBy(group =>
                                 group.Key.Category)
                             .ThenBy(group =>
                                 group.Key.Code))
                {
                    Console.WriteLine(
                        $"{group.Key.Severity}: " +
                        $"{group.Key.Category}/" +
                        $"{group.Key.Code} " +
                        $"({group.Count()})");
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                errorCount == 0
                    ? "Repository loaded successfully."
                    : "Repository loaded with validation errors.");

            return errorCount > 0 ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Unable to load repository: " +
                exception.Message);

            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");

        Console.WriteLine(
            "  UnifiedToolkit repository <repo-folder>");
    }

    private static int SeverityOrder(string severity)
    {
        return severity switch
        {
            "Error" => 0,
            "Warning" => 1,
            _ => 2
        };
    }
}