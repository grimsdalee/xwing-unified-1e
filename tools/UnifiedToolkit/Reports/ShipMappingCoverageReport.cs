using System.Text;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Reports;

public static class ShipMappingCoverageReport
{
    public static void Write(IEnumerable<ShipMappingCoverageEntry> entries, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("Status,SourceId,SourceName,SourceSize,MappingId,Kind,TargetId,TargetName,ExclusionReason");

        foreach (var entry in entries)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(entry.Status),
                Csv(entry.SourceId),
                Csv(entry.SourceName),
                Csv(entry.SourceSize),
                Csv(entry.MappingId),
                Csv(entry.Kind?.ToString() ?? ""),
                Csv(entry.TargetId),
                Csv(entry.TargetName),
                Csv(entry.ExclusionReason)
            }));
        }
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
