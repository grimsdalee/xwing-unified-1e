using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class PilotsReport
{
    public static void Write(
        IEnumerable<PilotDefinition> pilots,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(pilots);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Id,Name,Title,Faction,ShipType,ShipName," +
            "Initiative,Limited,Force,Charges,ShieldModifier," +
            "Texture,Docking,Actions,Keywords,AddedSlots,Linked");

        foreach (var pilot in pilots)
        {
            builder.AppendLine(string.Join(",",
                Csv(pilot.Id),
                Csv(pilot.Name),
                Csv(pilot.Title),
                Csv(pilot.Faction),
                Csv(pilot.ShipType),
                Csv(pilot.Ship?.Name),
                Csv(pilot.Initiative.ToString()),
                Csv(pilot.Limited.ToString()),
                Csv(pilot.Force.ToString()),
                Csv(pilot.Charges.ToString()),
                Csv(pilot.ShieldModifier.ToString()),
                Csv(pilot.Texture),
                Csv(pilot.Docking.ToString()),
                Csv(string.Join(" | ", pilot.Actions)),
                Csv(string.Join(" | ", pilot.Keywords)),
                Csv(string.Join(" | ", pilot.AddedSlots)),
                Csv(pilot.IsLinkedToShip.ToString())));
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