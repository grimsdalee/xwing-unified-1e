using System.Text;
using UnifiedToolkit.Conversion.Mapping.Pilots;

namespace UnifiedToolkit.Reports;

public static class PilotMappingCandidatesReport
{
    public static string Write(string reportsFolder, IEnumerable<PilotMappingCandidate> entries)
    {
        Directory.CreateDirectory(reportsFolder);
        var path = Path.Combine(reportsFolder, "official-first-edition-pilot-matches.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("SourceId,SourceName,SourceShipId,SourceFaction,SourceInitiative,TargetId,TargetName,TargetShipId,TargetFaction,TargetPilotSkill,TargetSquadPointCost,TargetUnique,TargetUpgradeSlots,Status,MatchMethod,Confidence,Notes");
        foreach (var x in entries)
        {
            writer.WriteLine(string.Join(',', new[]
            {
                x.SourceId, x.SourceName, x.SourceShipId, x.SourceFaction, x.SourceInitiative.ToString(),
                x.TargetId, x.TargetName, x.TargetShipId, x.TargetFaction, x.TargetPilotSkill.ToString(),
                x.TargetSquadPointCost.ToString(), x.TargetUnique.ToString(), x.TargetUpgradeSlots,
                x.Status, x.MatchMethod, x.Confidence.ToString("0.00"), x.Notes
            }.Select(Csv)));
        }
        return path;
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value}\"" : value;
    }
}
