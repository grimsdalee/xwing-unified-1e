using UnifiedToolkit.Reports;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class ShipsCommand
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

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports");

            var reportPath = Path.Combine(
                reportsFolder,
                "ships.csv");

            ShipsReport.Write(ships, reportPath);

            PrintSummary(repoFolder, reportPath, ships);

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Unable to parse ships: {exception.Message}");

            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  UnifiedToolkit ships <repo-folder>");
    }

    private static void PrintSummary(
        string repoFolder,
        string reportPath,
        IReadOnlyCollection<ShipDefinition> ships)
    {
        Console.WriteLine("UnifiedToolkit Ships");
        Console.WriteLine("====================");
        Console.WriteLine();

        Console.WriteLine($"Repo folder:    {repoFolder}");
        Console.WriteLine($"Ships found:    {ships.Count}");
        Console.WriteLine($"Report written: {reportPath}");
        Console.WriteLine();

        foreach (var ship in ships.Take(PreviewCount))
        {
            Console.WriteLine($"{ship.Name} [{ship.Id}]");
            Console.WriteLine($"  Size:     {ship.Size}");
            Console.WriteLine(
                $"  Stats:    Hull {ship.Hull}, " +
                $"Shield {ship.Shield}, " +
                $"Agility {ship.Agility}");

            Console.WriteLine(
                $"  Factions: {string.Join(", ", ship.Factions)}");

            Console.WriteLine();
        }

        if (ships.Count > PreviewCount)
        {
            Console.WriteLine(
                $"Showing first {PreviewCount} of " +
                $"{ships.Count} ships.");
        }
    }
}