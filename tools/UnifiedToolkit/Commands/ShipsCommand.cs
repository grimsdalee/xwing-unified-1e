using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class ShipsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  UnifiedToolkit ships <repo-folder>");
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);

        if (!Directory.Exists(repoFolder))
        {
            Console.WriteLine($"Repo folder not found: {repoFolder}");
            return 1;
        }

        var ships = ShipParser.ParseFromRepo(repoFolder);

        var reportsFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports");
        Directory.CreateDirectory(reportsFolder);

        var reportPath = Path.Combine(reportsFolder, "ships.csv");
        WriteShipsCsv(ships, reportPath);

        Console.WriteLine("UnifiedToolkit Ships");
        Console.WriteLine("====================");
        Console.WriteLine();

        Console.WriteLine($"Repo folder:    {repoFolder}");
        Console.WriteLine($"Ships found:    {ships.Count}");
        Console.WriteLine($"Report written: {reportPath}");
        Console.WriteLine();

        foreach (var ship in ships.Take(30))
        {
            Console.WriteLine($"{ship.Name} [{ship.Id}]");
            Console.WriteLine($"  Size:     {ship.Size}");
            Console.WriteLine($"  Stats:    Hull {ship.Hull}, Shield {ship.Shield}, Agility {ship.Agility}");
            Console.WriteLine($"  Factions: {string.Join(", ", ship.Factions)}");
            Console.WriteLine();
        }

        if (ships.Count > 30)
            Console.WriteLine($"Showing first 30 of {ships.Count} ships.");

        return 0;
    }

    private static void WriteShipsCsv(List<ShipDefinition> ships, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Id,Name,Size,Hull,Shield,Agility,Factions");

        foreach (var ship in ships)
        {
            sb.AppendLine(string.Join(",",
                Csv(ship.Id),
                Csv(ship.Name),
                Csv(ship.Size),
                Csv(ship.Hull.ToString()),
                Csv(ship.Shield.ToString()),
                Csv(ship.Agility.ToString()),
                Csv(string.Join(" | ", ship.Factions))));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Csv(string value)
    {
        value ??= "";

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            value = $"\"{value}\"";

        return value;
    }
}