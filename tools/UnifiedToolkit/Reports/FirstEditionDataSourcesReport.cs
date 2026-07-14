using System.Text;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;

namespace UnifiedToolkit.Reports;

public static class FirstEditionDataSourcesReport
{
    public static string Write(string outputFolder, IReadOnlyList<FirstEditionDataSourceFile> files)
    {
        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, "first-edition-data-sources.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Path,DataType,RecordsRead,Notes");
        foreach (var file in files)
            writer.WriteLine(string.Join(",", Csv(file.Path), Csv(file.DataType), file.RecordsRead, Csv(file.Notes)));
        return path;
    }

    private static string Csv(string value)
    {
        value ??= "";
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }
}
