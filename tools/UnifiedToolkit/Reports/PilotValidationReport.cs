using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class PilotValidationReport
{
    public static void Write(
        IEnumerable<PilotValidationIssue> issues,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException(
                "An output path is required.",
                nameof(outputPath));
        }

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Severity,Code,PilotId,PilotName,Faction," +
            "ShipType,ShipName,Message");

        foreach (var issue in issues)
        {
            builder.AppendLine(string.Join(",",
                Csv(issue.Severity),
                Csv(issue.Code),
                Csv(issue.PilotId),
                Csv(issue.PilotName),
                Csv(issue.Faction),
                Csv(issue.ShipType),
                Csv(issue.ShipName),
                Csv(issue.Message)));
        }

        File.WriteAllText(
            outputPath,
            builder.ToString(),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') ||
            value.Contains('"') ||
            value.Contains('\n') ||
            value.Contains('\r'))
        {
            value = $"\"{value}\"";
        }

        return value;
    }
}