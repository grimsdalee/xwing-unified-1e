using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Mapping.Dispositions;

namespace UnifiedToolkit.Reports;

public static class ShipDispositionReviewReport
{
    public static void WriteCsv(IEnumerable<ShipDispositionReviewEntry> entries, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("SourceId,SourceName,SourceFactions,SourceSize,SourceHull,SourceShields,SourceAgility,Kind,ProposedTargetId,Reason,Notes");
        foreach (var e in entries)
            writer.WriteLine(string.Join(",", new[] { Csv(e.SourceId), Csv(e.SourceName), Csv(e.SourceFactions), Csv(e.SourceSize), e.SourceHull.ToString(), e.SourceShields.ToString(), e.SourceAgility.ToString(), Csv(e.Kind.ToString()), Csv(e.ProposedTargetId), Csv(e.Reason), Csv(e.Notes) }));
    }

    public static void WriteJson(IEnumerable<ShipDispositionReviewEntry> entries, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(entries, options) + Environment.NewLine);
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
