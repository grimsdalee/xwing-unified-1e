using System.Text;
using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.Reports;

public static class IgnoredLuaEntitiesReport
{
    public static void Write(
        IEnumerable<LuaEntityClassification> classifications,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var ignored = classifications
            .Where(item => !item.IsSemanticCandidate)
            .OrderBy(item => item.Entity.SourceIndex)
            .ToList();

        var builder = new StringBuilder();

        builder.AppendLine(
            "Id,Classification,Reason,SourceIndex,Fields");

        foreach (var item in ignored)
        {
            var fields = string.Join(
                " | ",
                item.Entity.Fields.Select(
                    field =>
                        $"{field.Key}=" +
                        $"{LuaValueFormatter.Format(field.Value)}"));

            builder.AppendLine(string.Join(",",
                Csv(item.Entity.Id),
                Csv(item.Classification),
                Csv(item.Reason),
                Csv(item.Entity.SourceIndex.ToString()),
                Csv(fields)));
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