using System.Text;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class UpgradeRestrictionsReport
{
    public static void Write(
        IEnumerable<UpgradeRestrictionEntry> entries,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "UpgradeId,UpgradeName,Slot,Path," +
            "ValueKind,Value");

        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(",",
                Csv(entry.UpgradeId),
                Csv(entry.UpgradeName),
                Csv(entry.Slot),
                Csv(entry.Path),
                Csv(entry.ValueKind),
                Csv(entry.Value)));
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