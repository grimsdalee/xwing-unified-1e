using System.Text;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;

namespace UnifiedToolkit.Reports;

public static class FirstEditionUpgradesReport
{
    public static void Write(IEnumerable<FirstEditionUpgrade> upgrades, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(
            "Id,Name,Slot,SquadPointCost,Unique,Factions,ShipRestrictions,SizeRestrictions," +
            "SourceId,MappingId,Kind,MappingVersion,Text");

        foreach (var upgrade in upgrades.OrderBy(x => x.Slot).ThenBy(x => x.Name))
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(upgrade.Id),
                Csv(upgrade.Name),
                Csv(upgrade.Slot),
                upgrade.SquadPointCost.ToString(),
                upgrade.Unique.ToString(),
                Csv(string.Join("|", upgrade.Factions)),
                Csv(string.Join("|", upgrade.ShipRestrictions)),
                Csv(string.Join("|", upgrade.SizeRestrictions)),
                Csv(upgrade.Provenance.SourceId),
                Csv(upgrade.Provenance.MappingId),
                Csv(upgrade.Provenance.Kind.ToString()),
                Csv(upgrade.Provenance.MappingVersion),
                Csv(upgrade.Text)
            }));
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;

        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value}\""
            : value;
    }
}
