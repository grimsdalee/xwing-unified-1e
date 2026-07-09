using UnifiedToolkit.Models;

namespace UnifiedToolkit.Repo;

public static class RepoScanner
{
    public static List<RepoFileEntry> Scan(string repoFolder)
    {
        repoFolder = Path.GetFullPath(repoFolder);

        if (!Directory.Exists(repoFolder))
            throw new DirectoryNotFoundException($"Repo folder not found: {repoFolder}");

        var ignoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            "bin",
            "obj",
            "node_modules"
        };

        var files = Directory
            .EnumerateFiles(repoFolder, "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => ignoredFolders.Contains(part)))
            .ToList();

        var result = new List<RepoFileEntry>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(repoFolder, file);
            var extension = Path.GetExtension(file).ToLowerInvariant();

            result.Add(new RepoFileEntry
            {
                Path = relativePath,
                Extension = extension,
                Category = Categorise(extension, relativePath),
                SizeBytes = new FileInfo(file).Length,
                LineCount = CountLines(file, extension)
            });
        }

        return result
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Path)
            .ToList();
    }

    private static string Categorise(string extension, string path)
    {
        return extension switch
        {
            ".lua" => "Lua",
            ".xml" => "XML",
            ".json" => "JSON",
            ".png" or ".jpg" or ".jpeg" or ".webp" => "Image",
            ".obj" or ".fbx" or ".dae" or ".stl" => "Model",
            ".ttslua" => "TTS Lua",
            ".md" or ".txt" => "Text",
            _ => "Other"
        };
    }

    private static int CountLines(string path, string extension)
    {
        if (extension is not (".lua" or ".xml" or ".json" or ".md" or ".txt" or ".ttslua"))
            return 0;

        try
        {
            return File.ReadLines(path).Count();
        }
        catch
        {
            return 0;
        }
    }
}