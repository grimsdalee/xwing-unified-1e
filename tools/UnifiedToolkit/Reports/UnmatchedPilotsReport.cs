using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class UnmatchedPilotsReport
{
    public static void Write(
        IEnumerable<PilotDefinition> pilots,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(pilots);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var unmatched = pilots
            .Where(pilot => !pilot.IsLinkedToShip)
            .OrderBy(pilot => pilot.ShipType)
            .ThenBy(pilot => pilot.Name)
            .ToList();

        var builder = new StringBuilder();

        builder.AppendLine(
            "Id,Name,Faction,ShipType,Reason");

        foreach (var pilot in unmatched)
        {
            var reason = string.IsNullOrWhiteSpace(
                pilot.ShipType)
                ? "Pilot has no ship_type"
                : "No matching ShipDb entry";

            builder.AppendLine(string.Join(",",
                Csv(pilot.Id),
                Csv(pilot.Name),
                Csv(pilot.Faction),
                Csv(pilot.ShipType),
                Csv(reason)));
        }

        File.WriteAllText(
            outputPath,
            builder.ToString(),
            new UTF8Encoding(false));
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') ||
            value.Contains('"') ||
            value.Contains('\n') ||
            value.Contains('\r'))
        {
            value = $"\"{value}\"";
        }

        return value;
    }
}