using System.Text;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;

namespace UnifiedToolkit.Reports;

public static class OfficialAliasCandidatesReport
{
    public static string Write(string outputFolder, IReadOnlyList<OfficialAliasCandidate> candidates)
    {
        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, "official-alias-candidates.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("SourceId,SourceName,SourceFactions,SourceSize,TargetId,TargetName,TargetFactions,TargetSize,Attack,Agility,Hull,Shields,MatchMethod,Confidence,Decision,Notes");

        foreach (var x in candidates)
        {
            writer.WriteLine(string.Join(",",
                Csv(x.Source.Id),
                Csv(x.Source.Name),
                Csv(string.Join(";", x.Source.Factions)),
                Csv(x.Source.Size),
                Csv(x.Target?.Id ?? x.SuggestedTargetId),
                Csv(x.Target?.Name ?? ""),
                Csv(x.Target is null ? "" : string.Join(";", x.Target.Factions)),
                Csv(x.Target?.Size ?? ""),
                x.Target?.Attack.ToString() ?? "",
                x.Target?.Agility.ToString() ?? "",
                x.Target?.Hull.ToString() ?? "",
                x.Target?.Shields.ToString() ?? "",
                Csv(x.MatchMethod),
                x.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Csv(x.Decision),
                Csv(x.Notes)));
        }

        return path;
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
