using UnifiedToolkit.Reports;
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
        var objects = game.AllObjects().ToList();

        var reportsFolder = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + "_reports");

        Directory.CreateDirectory(reportsFolder);

        var reports = new List<IReport>
        {
            new ContainersReport(),
            new HierarchyReport()
        };

        foreach (var report in reports)
        {
            report.Generate(game, objects, reportsFolder);
        }

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
        Console.WriteLine("Reports written:");

        foreach (var report in reports)
        {
            Console.WriteLine($"  {Path.Combine(reportsFolder, report.FileName)}");
        }

        return 0;
    }
}