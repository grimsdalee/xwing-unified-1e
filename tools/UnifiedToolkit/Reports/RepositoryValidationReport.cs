using System.Text;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Reports;

public static class RepositoryValidationReport
{
    public static void Write(
        IEnumerable<RepositoryValidationIssue> issues,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Severity,Category,Code,EntityType," +
            "EntityId,EntityName,FieldName,Message");

        foreach (var issue in issues)
        {
            builder.AppendLine(string.Join(",",
                Csv(issue.Severity),
                Csv(issue.Category),
                Csv(issue.Code),
                Csv(issue.EntityType),
                Csv(issue.EntityId),
                Csv(issue.EntityName),
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