using System.Text;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Reports;

public static class UpgradeMappingCoverageReport
{
    public static void Write(IEnumerable<UpgradeMappingCoverageEntry> rows, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(
            "SourceId,SourceName,SourceSlot,Status,CanonicalSourceId,TargetId,TargetSlot,Notes");

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(row.SourceId),
                Csv(row.SourceName),
                Csv(row.SourceSlot),
                Csv(row.Status),
                Csv(row.CanonicalSourceId),
                Csv(row.TargetId),
                Csv(row.TargetSlot),
                Csv(row.Notes)
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
