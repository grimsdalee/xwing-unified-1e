using System.Text;

namespace UnifiedToolkit.Reports;

public abstract class CsvReport : IReport
{
    public abstract string FileName { get; }

    public abstract void Generate(
        Models.TtsGame game,
        IReadOnlyList<Models.TtsObject> objects,
        string reportsFolder);

    protected static string Csv(string value)
    {
        value ??= "";

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

    protected static StreamWriter CreateWriter(string reportsFolder, string filename)
    {
        Directory.CreateDirectory(reportsFolder);

        return new StreamWriter(
            Path.Combine(reportsFolder, filename),
            false,
            Encoding.UTF8);
    }
}