using System.Text;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking.Reports;

public sealed class LegacyAssetContextReportWriter
{
    public void Write(string outputRoot, LegacyAssetContextIndex index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(index);

        var reportsFolder = Path.Combine(outputRoot, "reports");
        Directory.CreateDirectory(reportsFolder);

        var allContexts = index.All;
        WriteCsv(
            Path.Combine(reportsFolder, "legacy-asset-contexts.csv"),
            allContexts);

        var dialContexts = allContexts
            .Where(context => ContainsAny(
                context.SearchText,
                "dial",
                "maneuver",
                "manoeuvre",
                "huge",
                "epic",
                "cro-c",
                "c-roc",
                "gozanti",
                "gr-75",
                "gr75"))
            .ToList();

        WriteCsv(
            Path.Combine(reportsFolder, "legacy-dial-contexts.csv"),
            dialContexts);
    }

    private static void WriteCsv(string path, IReadOnlyCollection<LegacyAssetContext> contexts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SourceUrl,PropertyName,ObjectName,ObjectNickname,ObjectGuid,ObjectDescription,ContainerText,JsonPath");

        foreach (var context in contexts
                     .OrderBy(item => item.ObjectNickname, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.PropertyName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.JsonPath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(Csv(context.SourceUrl)).Append(',')
                .Append(Csv(context.PropertyName)).Append(',')
                .Append(Csv(context.ObjectName)).Append(',')
                .Append(Csv(context.ObjectNickname)).Append(',')
                .Append(Csv(context.ObjectGuid)).Append(',')
                .Append(Csv(context.ObjectDescription)).Append(',')
                .Append(Csv(context.ContainerText)).Append(',')
                .Append(Csv(context.JsonPath))
                .AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Csv(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
