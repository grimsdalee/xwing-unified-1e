using System.Text;
using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Reports;

public static class ConversionIssuesReport
{
    public static void Write(IEnumerable<ConversionIssue> issues, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("Severity,Category,Code,SourceType,SourceId,SourceName,TargetId,Message");

        foreach (var issue in issues)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(issue.Severity), Csv(issue.Category), Csv(issue.Code), Csv(issue.SourceType),
                Csv(issue.SourceId), Csv(issue.SourceName), Csv(issue.TargetId), Csv(issue.Message)
            }));
        }
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
