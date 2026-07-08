using System.Text;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Commands;

public static class AnalyseCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  UnifiedToolkit analyse <tts-json-file>");
            return 1;
        }

        var inputPath = Path.GetFullPath(args[0]);

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        var game = TtsSaveLoader.Load(inputPath);
        var objects = Flatten(game.Objects).ToList();

        var reportsFolder = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + "_reports");

        Directory.CreateDirectory(reportsFolder);

        WriteObjectsCsv(objects, Path.Combine(reportsFolder, "objects.csv"));
        WriteObjectTypesCsv(objects, Path.Combine(reportsFolder, "object-types.csv"));

        Console.WriteLine("UnifiedToolkit Analysis");
        Console.WriteLine("=======================");
        Console.WriteLine();

        Console.WriteLine($"Input file:      {inputPath}");
        Console.WriteLine($"Reports folder:  {reportsFolder}");
        Console.WriteLine();

        Console.WriteLine("Objects");
        Console.WriteLine("-------");
        Console.WriteLine($"Total objects:       {objects.Count}");
        Console.WriteLine($"Top-level objects:   {game.Objects.Count}");
        Console.WriteLine($"Objects with Lua:    {objects.Count(x => x.HasLua)}");
        Console.WriteLine($"Objects with XML:    {objects.Count(x => x.HasXml)}");
        Console.WriteLine();

        Console.WriteLine("Object types");
        Console.WriteLine("------------");

        foreach (var group in objects
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.Type) ? "(blank)" : x.Type)
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key))
        {
            Console.WriteLine($"{group.Key,-30} {group.Count(),5}");
        }

        Console.WriteLine();
        Console.WriteLine("CSV reports written:");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "objects.csv")}");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "object-types.csv")}");

        return 0;
    }

    private static IEnumerable<TtsObject> Flatten(IEnumerable<TtsObject> objects)
    {
        foreach (var obj in objects)
        {
            yield return obj;

            foreach (var child in Flatten(obj.Children))
                yield return child;
        }
    }

    private static void WriteObjectsCsv(List<TtsObject> objects, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Guid,Nickname,Description,GMNotes,Type,ParentGuid,ParentNickname,ChildCount,HasLua,HasXml");

        foreach (var obj in objects)
        {
            sb.AppendLine(string.Join(",",
                Csv(obj.Guid),
                Csv(obj.Nickname),
                Csv(obj.Description),
                Csv(obj.GMNotes),
                Csv(obj.Type),
                Csv(obj.Parent?.Guid ?? ""),
                Csv(obj.Parent?.Nickname ?? ""),
                Csv(obj.Children.Count.ToString()),
                Csv(obj.HasLua.ToString()),
                Csv(obj.HasXml.ToString())));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteObjectTypesCsv(List<TtsObject> objects, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Type,Count,WithLua,WithXml");

        foreach (var group in objects
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.Type) ? "(blank)" : x.Type)
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key))
        {
            sb.AppendLine(string.Join(",",
                Csv(group.Key),
                Csv(group.Count().ToString()),
                Csv(group.Count(x => x.HasLua).ToString()),
                Csv(group.Count(x => x.HasXml).ToString())));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Csv(string value)
    {
        value ??= "";

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            value = $"\"{value}\"";

        return value;
    }
}