using System.Text;
using UnifiedToolkit.Models;
using UnifiedToolkit.Repo;
using UnifiedToolkit.Reports;

namespace UnifiedToolkit.Commands;

public static class RepoCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  UnifiedToolkit repo <repo-folder>");
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);

        if (!Directory.Exists(repoFolder))
        {
            Console.WriteLine($"Repo folder not found: {repoFolder}");
            return 1;
        }

        var files = RepoScanner.Scan(repoFolder);

        var luaFiles = files
            .Where(x => x.Extension == ".lua" || x.Extension == ".ttslua")
            .Select(x => LuaParser.Parse(repoFolder, x))
            .ToList();

        var reportsFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports");
        Directory.CreateDirectory(reportsFolder);

        WriteRepoFilesCsv(files, Path.Combine(reportsFolder, "repo-files.csv"));
        WriteRepoSummaryCsv(files, Path.Combine(reportsFolder, "repo-summary.csv"));
        new RepoStructureReport().Generate(files, reportsFolder);
        new RepoLuaReport().Generate(files, repoFolder, reportsFolder);
        new RepoSourceReport().Generate(luaFiles, reportsFolder);

        Console.WriteLine("UnifiedToolkit Repo Scan");
        Console.WriteLine("========================");
        Console.WriteLine();

        Console.WriteLine($"Repo folder:     {repoFolder}");
        Console.WriteLine($"Reports folder:  {reportsFolder}");
        Console.WriteLine();

        Console.WriteLine("Files");
        Console.WriteLine("-----");
        Console.WriteLine($"Total files: {files.Count}");
        Console.WriteLine();

        foreach (var group in files
                     .GroupBy(x => x.Category)
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key))
        {
            Console.WriteLine($"{group.Key,-12} {group.Count(),5}");
        }

        Console.WriteLine();
        Console.WriteLine("Reports written:");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "repo-files.csv")}");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "repo-summary.csv")}");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "repo-folders.csv")}");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "repo-lua.csv")}");
        Console.WriteLine($"  {Path.Combine(reportsFolder, "repo-source.csv")}");

        return 0;
    }

    private static void WriteRepoFilesCsv(List<RepoFileEntry> files, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Path,Extension,Category,SizeBytes,LineCount");

        foreach (var file in files)
        {
            sb.AppendLine(string.Join(",",
                Csv(file.Path),
                Csv(file.Extension),
                Csv(file.Category),
                Csv(file.SizeBytes.ToString()),
                Csv(file.LineCount.ToString())));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteRepoSummaryCsv(List<RepoFileEntry> files, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Category,FileCount,TotalSizeBytes,TotalLineCount");

        foreach (var group in files
                     .GroupBy(x => x.Category)
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key))
        {
            sb.AppendLine(string.Join(",",
                Csv(group.Key),
                Csv(group.Count().ToString()),
                Csv(group.Sum(x => x.SizeBytes).ToString()),
                Csv(group.Sum(x => x.LineCount).ToString())));
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