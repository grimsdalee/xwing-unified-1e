using System.Text.RegularExpressions;
using UnifiedToolkit.Models;

namespace UnifiedToolkit.Repo;

public static class LuaParser
{
    private static readonly Regex FunctionRegex = new(
        @"function\s+([A-Za-z0-9_:\.]+)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex RequireRegex = new(
        @"require\s*\(?\s*[""']([^""']+)[""']\s*\)?",
        RegexOptions.Compiled);

    public static LuaFileInfo Parse(string repoFolder, RepoFileEntry file)
    {
        var fullPath = Path.Combine(repoFolder, file.Path);
        var text = File.ReadAllText(fullPath);

        var info = new LuaFileInfo
        {
            Path = file.Path,
            Folder = GetFolder(file.Path),
            LineCount = file.LineCount,

            UsesSpawnObject = Contains(text, "spawnObject"),
            UsesSpawnObjectData = Contains(text, "spawnObjectData"),
            UsesTakeObject = Contains(text, "takeObject"),
            UsesCreateButton = Contains(text, "createButton"),
            UsesJsonDecode = Contains(text, "JSON.decode"),
            UsesJsonEncode = Contains(text, "JSON.encode")
        };

        foreach (Match match in FunctionRegex.Matches(text))
        {
            var name = match.Groups[1].Value;

            if (!info.Functions.Contains(name))
                info.Functions.Add(name);
        }

        foreach (Match match in RequireRegex.Matches(text))
        {
            var name = match.Groups[1].Value;

            if (!info.Requires.Contains(name))
                info.Requires.Add(name);
        }

        info.Functions.Sort();
        info.Requires.Sort();

        return info;
    }

    private static bool Contains(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFolder(string path)
    {
        var folder = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(folder) ? "." : folder.Replace('\\', '/');
    }
}