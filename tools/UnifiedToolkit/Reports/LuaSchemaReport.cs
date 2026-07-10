using System.Text;
using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.Reports;

public static class LuaSchemaReport
{
    public static void Write(
        LuaDatabaseSchema schema,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Field,Occurrences,CoveragePercent," +
            "ValueKinds,MixedTypes,Examples");

        foreach (var field in schema.Fields)
        {
            var coverage = schema.EntityCount == 0
                ? 0
                : field.OccurrenceCount * 100m /
                  schema.EntityCount;

            var kinds = string.Join(
                " | ",
                field.ValueKinds
                    .OrderBy(kind => kind)
                    .Select(kind => kind.ToString()));

            var examples = string.Join(
                " | ",
                field.ExampleValues);

            builder.AppendLine(string.Join(",",
                Csv(field.FieldName),
                Csv(field.OccurrenceCount.ToString()),
                Csv(coverage.ToString("0.00")),
                Csv(kinds),
                Csv(field.HasMixedTypes.ToString()),
                Csv(examples)));
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