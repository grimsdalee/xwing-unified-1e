using System.Text;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;

namespace UnifiedToolkit.Reports;

public static class OfficialShipMatchesReport
{
    public static string Write(string reportsFolder, IReadOnlyList<OfficialShipMatch> matches)
    {
        Directory.CreateDirectory(reportsFolder);
        var path = Path.Combine(reportsFolder, "official-first-edition-ship-matches.csv");

        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("SourceId,SourceName,SourceFaction,SourceSize,TargetId,TargetName,TargetFaction,TargetSize,Attack,Agility,Hull,Shields,Actions,MatchMethod,Confidence,Decision,Notes");

        foreach (var match in matches)
        {
            writer.WriteLine(string.Join(",",
                Csv(match.Source.Id),
                Csv(match.Source.Name),
                Csv(string.Join(";", match.Source.Factions)),
                Csv(match.Source.Size),
                Csv(match.Target?.Id ?? ""),
                Csv(match.Target?.Name ?? ""),
                Csv(string.Join(";", match.Target?.Factions ?? new List<string>())),
                Csv(match.Target?.Size ?? ""),
                match.Target?.Attack.ToString() ?? "",
                match.Target?.Agility.ToString() ?? "",
                match.Target?.Hull.ToString() ?? "",
                match.Target?.Shields.ToString() ?? "",
                Csv(string.Join(";", match.Target?.Actions ?? new List<string>())),
                Csv(match.MatchMethod),
                match.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Csv(match.Decision),
                Csv(match.Notes)));
        }

        return path;
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value}\"" : value;
    }
}
