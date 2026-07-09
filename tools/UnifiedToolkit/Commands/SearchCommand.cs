using UnifiedToolkit.Models;
using UnifiedToolkit.Repo;
using UnifiedToolkit.TTS;
using UnifiedToolkit.Shared;

namespace UnifiedToolkit.Commands;

public static class SearchCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  UnifiedToolkit search <tts-json-file> <repo-folder> <text>");
            return 1;
        }

        var ttsJsonPath = Path.GetFullPath(args[0]);
        var repoFolder = Path.GetFullPath(args[1]);
        var searchText = string.Join(" ", args.Skip(2));

        if (!File.Exists(ttsJsonPath))
        {
            Console.WriteLine($"TTS JSON file not found: {ttsJsonPath}");
            return 1;
        }

        if (!Directory.Exists(repoFolder))
        {
            Console.WriteLine($"Repo folder not found: {repoFolder}");
            return 1;
        }

        Console.WriteLine("Loading TTS save...");
        var game = TtsSaveLoader.Load(ttsJsonPath);
        var objects = game.AllObjects().ToList();

        Console.WriteLine("Loading source repo...");
        var source = SourceModelBuilder.Build(repoFolder);

        Console.WriteLine();

        PrintHeader(searchText);
        PrintTtsMatches(objects, searchText);
        PrintSourceMatches(source, searchText);

        return 0;
    }

    private static void PrintHeader(string searchText)
    {
        Console.WriteLine("UnifiedToolkit Search");
        Console.WriteLine("=====================");
        Console.WriteLine();
        Console.WriteLine($"Search: {searchText}");
        Console.WriteLine();
    }

    private static void PrintTtsMatches(IReadOnlyList<TtsObject> objects, string searchText)
    {
        var matches = objects
            .Where(x =>
                TextMatch.Contains(x.Guid, searchText) ||
                TextMatch.Contains(x.Nickname, searchText) ||
                TextMatch.Contains(x.Description, searchText) ||
                TextMatch.Contains(x.GMNotes, searchText) ||
                TextMatch.Contains(x.Type, searchText) ||
                TextMatch.Contains(x.Parent?.Nickname ?? "", searchText))
            .OrderBy(x => x.Parent?.Nickname ?? "")
            .ThenBy(x => x.Nickname)
            .Take(50)
            .ToList();

        Console.WriteLine("TTS OBJECTS");
        Console.WriteLine("-----------");

        if (matches.Count == 0)
        {
            Console.WriteLine("No matches.");
            Console.WriteLine();
            return;
        }

        foreach (var obj in matches)
        {
            var name = string.IsNullOrWhiteSpace(obj.Nickname) ? "(unnamed)" : obj.Nickname;
            var parent = string.IsNullOrWhiteSpace(obj.Parent?.Nickname)
                ? "(root)"
                : obj.Parent!.Nickname;

            Console.WriteLine($"{name}");
            Console.WriteLine($"  Type:     {obj.Type}");
            Console.WriteLine($"  GUID:     {obj.Guid}");
            Console.WriteLine($"  Parent:   {parent}");
            Console.WriteLine($"  Children: {obj.Children.Count}");
            Console.WriteLine($"  Lua/XML:  {obj.HasLua}/{obj.HasXml}");
            Console.WriteLine();
        }

        if (matches.Count == 50)
            Console.WriteLine("Showing first 50 TTS matches only.");
        
        Console.WriteLine();
    }

    private static void PrintSourceMatches(SourceModel source, string searchText)
    {
        var fileMatches = source.Files
            .Where(x =>
                TextMatch.Contains(x.Path, searchText) ||
                TextMatch.Contains(x.Category, searchText))
            .OrderBy(x => x.Path)
            .Take(50)
            .ToList();

        var luaMatches = source.LuaFiles
            .Where(x =>
                TextMatch.Contains(x.Path, searchText) ||
                x.Functions.Any(f => TextMatch.Contains(f, searchText)) ||
                x.Requires.Any(r => TextMatch.Contains(r, searchText)) ||
                FileContains(source.RepoFolder, x.Path, searchText))
            .OrderBy(x => x.Path)
            .Take(50)
            .ToList();

        Console.WriteLine("SOURCE FILES");
        Console.WriteLine("------------");

        if (fileMatches.Count == 0 && luaMatches.Count == 0)
        {
            Console.WriteLine("No matches.");
            Console.WriteLine();
            return;
        }

        foreach (var file in fileMatches)
        {
            Console.WriteLine($"{file.Path}");
            Console.WriteLine($"  Category: {file.Category}");
            Console.WriteLine($"  Size:     {file.SizeBytes} bytes");
            Console.WriteLine($"  Lines:    {file.LineCount}");
            Console.WriteLine();
        }

        Console.WriteLine("LUA MODULE MATCHES");
        Console.WriteLine("------------------");

        if (luaMatches.Count == 0)
        {
            Console.WriteLine("No Lua matches.");
            Console.WriteLine();
            return;
        }

        foreach (var lua in luaMatches)
        {
            Console.WriteLine($"{lua.Path}");
            Console.WriteLine($"  Folder:    {lua.Folder}");
            Console.WriteLine($"  Lines:     {lua.LineCount}");
            Console.WriteLine($"  Functions: {lua.Functions.Count}");
            Console.WriteLine($"  Requires:  {lua.Requires.Count}");

            var matchingFunctions = lua.Functions
                .Where(x => TextMatch.Contains(x, searchText))
                .Take(10)
                .ToList();

            if (matchingFunctions.Count > 0)
                Console.WriteLine($"  Matching functions: {string.Join(", ", matchingFunctions)}");

            Console.WriteLine();
        }

        if (fileMatches.Count == 50)
            Console.WriteLine("Showing first 50 source file matches only.");

        if (luaMatches.Count == 50)
            Console.WriteLine("Showing first 50 Lua matches only.");
    }    

    private static bool FileContains(string repoFolder, string relativePath, string searchText)
    {
        try
        {
            var fullPath = Path.Combine(repoFolder, relativePath);
            var text = File.ReadAllText(fullPath);
            return TextMatch.Contains(text, searchText);
        }
        catch
        {
            return false;
        }
    }
}