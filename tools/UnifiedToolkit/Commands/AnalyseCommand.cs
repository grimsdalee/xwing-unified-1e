using System.Text;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Commands;

public static class AnalyseCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  UnifiedToolkit analyse <extract-folder>");
            return 1;
        }

        var extractFolder = Path.GetFullPath(args[0]);
        var objectsFolder = Path.Combine(extractFolder, "objects");

        if (!Directory.Exists(objectsFolder))
        {
            Console.WriteLine($"Could not find objects folder: {objectsFolder}");
            Console.WriteLine("Run extract first.");
            return 1;
        }

        var reportsFolder = Path.Combine(extractFolder, "reports");
        Directory.CreateDirectory(reportsFolder);

        var objects = TtsExtractReader.ReadObjects(extractFolder);

        WriteObjectsCsv(objects, extractFolder, Path.Combine(reportsFolder, "objects.csv"));
        WriteObjectTypesCsv(objects, Path.Combine(reportsFolder, "object-types.csv"));

        Console.WriteLine("UnifiedToolkit Analysis");
        Console.WriteLine("=======================");
        Console.WriteLine();

        Console.WriteLine($"Extract folder: {extractFolder}");
        Console.WriteLine($"Reports folder: {reportsFolder}");
        Console.WriteLine();

        Console.WriteLine("Objects");
        Console.WriteLine("-------");
        Console.WriteLine($"Total objects:       {objects.Count}");
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

    private static void WriteObjectsCsv(List<TtsExtractedObject> objects, string extractFolder, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Guid,Name,Description,GMNotes,Type,Folder,ContainedCount,CardID,HasLua,HasXml");

        foreach (var obj in objects)
        {
            sb.AppendLine(string.Join(",",
                Csv(obj.Guid),
                Csv(obj.Name),
                Csv(obj.Description),
                Csv(obj.GMNotes),
                Csv(obj.Type),
                Csv(Path.GetRelativePath(extractFolder, obj.Folder)),
                Csv(obj.ContainedCount.ToString()),
                Csv(obj.CardID),
                Csv(obj.HasLua.ToString()),
                Csv(obj.HasXml.ToString())));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteObjectTypesCsv(List<TtsExtractedObject> objects, string path)
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