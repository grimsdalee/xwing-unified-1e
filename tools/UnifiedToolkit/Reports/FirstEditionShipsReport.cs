using System.Text;
using UnifiedToolkit.Conversion.FirstEdition;

namespace UnifiedToolkit.Reports;

public static class FirstEditionShipsReport
{
    public static void Write(IEnumerable<FirstEditionShip> ships, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("Id,Name,Size,Attack,Agility,Hull,Shields,Factions,Actions,SourceId,MappingId,Kind,MappingVersion");

        foreach (var ship in ships.OrderBy(x => x.Name).ThenBy(x => x.Id))
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(ship.Id), Csv(ship.Name), Csv(ship.Size), ship.Attack.ToString(), ship.Agility.ToString(),
                ship.Hull.ToString(), ship.Shields.ToString(), Csv(string.Join("|", ship.Factions)),
                Csv(string.Join("|", ship.Actions)), Csv(ship.Provenance.SourceId), Csv(ship.Provenance.MappingId),
                Csv(ship.Provenance.Kind.ToString()), Csv(ship.Provenance.MappingVersion)
            }));
        }
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
