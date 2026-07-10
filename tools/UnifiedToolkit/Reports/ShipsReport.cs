using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class ShipsReport
{
    public static void Write(
        IEnumerable<ShipDefinition> ships,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(ships);

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(
                "An output path is required.",
                nameof(outputPath));

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Id,Name,Size,Hull,Shield,Agility,Factions");

        foreach (var ship in ships)
        {
            builder.AppendLine(string.Join(",",
                Csv(ship.Id),
                Csv(ship.Name),
                Csv(ship.Size),
                Csv(ship.Hull.ToString()),
                Csv(ship.Shield.ToString()),
                Csv(ship.Agility.ToString()),
                Csv(string.Join(" | ", ship.Factions))));
        }

        File.WriteAllText(
            outputPath,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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