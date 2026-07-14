using System.Text;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Reports;

public static class UpgradeSourceAlternatesReport
{
    public static string Write(string folder, IReadOnlyList<UpgradeSourceAlternate> rows)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "upgrade-source-alternates.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        WriteRow(writer, "SourceId", "CanonicalSourceId", "TargetId", "TargetSlot", "Relationship", "Notes");
        foreach (var x in rows) WriteRow(writer, x.SourceId, x.CanonicalSourceId, x.TargetId, x.TargetSlot, x.Relationship, x.Notes);
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
