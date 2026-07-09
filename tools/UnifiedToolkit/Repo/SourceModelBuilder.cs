using UnifiedToolkit.Models;

namespace UnifiedToolkit.Repo;

public static class SourceModelBuilder
{
    public static SourceModel Build(string repoFolder)
    {
        repoFolder = Path.GetFullPath(repoFolder);

        var files = RepoScanner.Scan(repoFolder);

        var luaFiles = files
            .Where(x => x.Extension == ".lua" || x.Extension == ".ttslua")
            .Select(x => LuaParser.Parse(repoFolder, x))
            .ToList();

        var model = new SourceModel
        {
            RepoFolder = repoFolder
        };

        model.Files.AddRange(files);
        model.LuaFiles.AddRange(luaFiles);

        foreach (var group in luaFiles
                     .GroupBy(x => GetModuleFolder(x.Path))
                     .OrderBy(x => x.Key))
        {
            var module = new ModuleInfo
            {
                Name = GetModuleName(group.Key),
                Folder = group.Key
            };

            module.Files.AddRange(group.OrderBy(x => x.Path));
            model.Modules.Add(module);
        }

        return model;
    }

    private static string GetModuleFolder(string path)
    {
        var normalised = path.Replace('\\', '/');

        if (!normalised.Contains('/'))
            return ".";

        var parts = normalised.Split('/');

        if (parts.Length >= 4 && parts[0] == "TTS_xwing" && parts[1] == "src")
        {
            if (parts[2] == "Game" && parts.Length >= 5)
                return $"TTS_xwing/src/Game/{parts[3]}";

            return $"TTS_xwing/src/{parts[2]}";
        }

        return Path.GetDirectoryName(normalised)?.Replace('\\', '/') ?? ".";
    }

    private static string GetModuleName(string folder)
    {
        if (folder == ".")
            return "Root";

        return folder.Split('/').Last();
    }
}