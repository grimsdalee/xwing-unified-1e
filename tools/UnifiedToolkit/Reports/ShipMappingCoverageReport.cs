using System.Text;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Reports;

public static class ShipMappingCoverageReport
{
    public static void Write(IEnumerable<ShipMappingCoverageEntry> entries, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("Status,SourceId,SourceName,SourceSize,MappingId,Kind,Disposition,TargetId,TargetName,Reason");
        foreach (var e in entries) writer.WriteLine(string.Join(",", new[] { Csv(e.Status), Csv(e.SourceId), Csv(e.SourceName), Csv(e.SourceSize), Csv(e.MappingId), Csv(e.Kind?.ToString() ?? ""), Csv(e.Disposition), Csv(e.TargetId), Csv(e.TargetName), Csv(e.Reason) }));
    }
    private static string Csv(string value) { value ??= ""; if (value.Contains('"')) value = value.Replace("\"", "\"\""); return value.IndexOfAny([',','"','\n','\r']) >= 0 ? $"\"{value}\"" : value; }
}
