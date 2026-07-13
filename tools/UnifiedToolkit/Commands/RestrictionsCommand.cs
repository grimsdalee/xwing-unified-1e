using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class RestrictionsCommand
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
            Console.WriteLine(
                "UnifiedToolkit Upgrade Restrictions");

            Console.WriteLine(
                "====================================");

            Console.WriteLine();
            Console.WriteLine(
                $"Repo folder: {repoFolder}");

            Console.WriteLine(
                "Loading repository...");

            Console.WriteLine();

            var repository =
                RepositoryLoader.Load(repoFolder);

            var upgradesWithRestrictions =
                repository.Upgrades.Count(
                    upgrade =>
                        upgrade.Restrictions is not null);

            var entries =
                UpgradeRestrictionFlattener.Flatten(
                    repository.Upgrades);

            var distinctPaths = entries
                .Select(entry => entry.Path)
                .Distinct(StringComparer.Ordinal)
                .Count();

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports");

            var reportPath = Path.Combine(
                reportsFolder,
                "upgrade-restrictions.csv");

            UpgradeRestrictionsReport.Write(
                entries,
                reportPath);

            Console.WriteLine(
                $"Upgrades loaded:             " +
                $"{repository.Upgrades.Count}");

            Console.WriteLine(
                $"Upgrades with restrictions:  " +
                $"{upgradesWithRestrictions}");

            Console.WriteLine(
                $"Flattened restriction values: " +
                $"{entries.Count}");

            Console.WriteLine(
                $"Distinct restriction paths:  " +
                $"{distinctPaths}");

            Console.WriteLine(
                $"Report written:              " +
                $"{reportPath}");

            Console.WriteLine();
            Console.WriteLine("Restriction preview");
            Console.WriteLine("-------------------");

            foreach (var entry in entries.Take(PreviewCount))
            {
                Console.WriteLine(
                    $"{entry.UpgradeName} [{entry.UpgradeId}]");

                Console.WriteLine(
                    $"  Slot:  {entry.Slot}");

                Console.WriteLine(
                    $"  Path:  {entry.Path}");

                Console.WriteLine(
                    $"  Value: {entry.ValueKind} " +
                    $"{entry.Value}");

                Console.WriteLine();
            }

            if (entries.Count > PreviewCount)
            {
                Console.WriteLine(
                    $"Showing first {PreviewCount} of " +
                    $"{entries.Count} restriction values.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Unable to inspect upgrade " +
                $"restrictions: {exception.Message}");

            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");

        Console.WriteLine(
            "  UnifiedToolkit restrictions " +
            "<repo-folder>");
    }
}