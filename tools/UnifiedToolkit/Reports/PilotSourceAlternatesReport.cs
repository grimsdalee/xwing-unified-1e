using System.Text;
using UnifiedToolkit.Conversion.Mapping.Pilots;

namespace UnifiedToolkit.Reports;

public static class PilotSourceAlternatesReport
{
    public static string Write(string folder, IEnumerable<PilotSourceAlternate> alternates)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "pilot-source-alternates.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("SourceId,CanonicalSourceId,TargetId,TargetShipId,TargetFaction,Relationship,Notes");
        foreach (var x in alternates)
            writer.WriteLine(string.Join(',', Q(x.SourceId), Q(x.CanonicalSourceId), Q(x.TargetId), Q(x.TargetShipId), Q(x.TargetFaction), Q(x.Relationship), Q(x.Notes)));
        return path;
    }

    private static string Q(string value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
