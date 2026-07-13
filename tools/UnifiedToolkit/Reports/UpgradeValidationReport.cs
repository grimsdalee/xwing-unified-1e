using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class UpgradeValidationReport
{
    public static void Write(
        IEnumerable<UpgradeValidationIssue> issues,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Severity,Code,UpgradeId,UpgradeName," +
            "Slot,FieldName,Message");

        foreach (var issue in issues)
        {
            builder.AppendLine(string.Join(",",
                Csv(issue.Severity),
                Csv(issue.Code),
                Csv(issue.UpgradeId),
                Csv(issue.UpgradeName),
                Csv(issue.Slot),
                Csv(issue.FieldName),
                Csv(issue.Message)));
        }

        File.WriteAllText(
            outputPath,
            builder.ToString(),
            new UTF8Encoding(false));
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