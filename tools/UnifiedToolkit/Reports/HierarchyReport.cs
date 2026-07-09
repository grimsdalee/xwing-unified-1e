using UnifiedToolkit.Models;

namespace UnifiedToolkit.Reports;

public sealed class HierarchyReport : IReport
{
    public string FileName => "hierarchy.txt";

    public void Generate(TtsGame game, IReadOnlyList<TtsObject> objects, string reportsFolder)
    {
        Directory.CreateDirectory(reportsFolder);

        var path = Path.Combine(reportsFolder, FileName);

        using var writer = new StreamWriter(path, false);

        writer.WriteLine("UnifiedToolkit Hierarchy Report");
        writer.WriteLine("===============================");
        writer.WriteLine();

        foreach (var obj in game.Objects.OrderBy(x => x.Nickname))
        {
            WriteObject(writer, obj, 0);
        }
    }

    private static void WriteObject(StreamWriter writer, TtsObject obj, int depth)
    {
        var indent = new string(' ', depth * 2);
        var name = string.IsNullOrWhiteSpace(obj.Nickname) ? "(unnamed)" : obj.Nickname;

        writer.WriteLine($"{indent}- {name} [{obj.Type}] ({obj.Children.Count} children)");

        foreach (var child in obj.Children.OrderBy(x => x.Nickname))
        {
            WriteObject(writer, child, depth + 1);
        }
    }
}