using System.Text;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Reports;

public static class UpgradeMappingCandidatesReport
{
    public static string Write(string folder, IReadOnlyList<UpgradeMappingCandidate> rows)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "official-first-edition-upgrade-matches.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        WriteRow(writer, "SourceId", "SourceName", "SourceSlot", "TargetId", "TargetName", "TargetSlot", "TargetCost", "TargetUnique", "Status", "MatchMethod", "Confidence", "Notes");
        foreach (var x in rows)
            WriteRow(writer, x.SourceId, x.SourceName, x.SourceSlot, x.TargetId, x.TargetName, x.TargetSlot, x.TargetCost.ToString(), x.TargetUnique.ToString(), x.Status, x.MatchMethod, x.Confidence.ToString("0.00"), x.Notes);
        return path;
    }

    private static void WriteRow(StreamWriter writer, params string[] values) => writer.WriteLine(string.Join(',', values.Select(Csv)));
    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
