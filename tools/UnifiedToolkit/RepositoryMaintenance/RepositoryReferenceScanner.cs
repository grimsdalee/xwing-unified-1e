using System.Text;

namespace UnifiedToolkit.RepositoryMaintenance;

public sealed class RepositoryReferenceScanner
{
    private static readonly HashSet<string> TextExtensions = new(
        new[]
        {
            ".cs", ".json", ".lua", ".xml", ".md", ".csv", ".txt",
            ".yml", ".yaml", ".toml", ".props", ".targets", ".sln",
            ".csproj", ".ps1", ".sh", ".bat", ".cmd"
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] IgnoredDirectoryNames =
    {
        ".git", ".vs", "bin", "obj", "node_modules",
        "_unifiedtoolkit_quarantine"
    };

    public IReadOnlyList<string> FindReferences(
        string repositoryRoot,
        string repositoryRelativePath)
    {
        var normalisedPath = Normalise(repositoryRelativePath);
        var fileName = Path.GetFileName(repositoryRelativePath);
        var results = new List<string>();

        foreach (var file in EnumerateCandidateFiles(repositoryRoot))
        {
            var relative = Normalise(Path.GetRelativePath(repositoryRoot, file));

            if (IsIgnoredMetadataFile(relative))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(file, Encoding.UTF8);
            }
            catch
            {
                continue;
            }

            var normalisedContent = content.Replace('\\', '/');
            if (normalisedContent.Contains(normalisedPath, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(fileName)
                    && normalisedContent.Contains(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(relative);
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (IgnoredDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;

                pending.Push(directory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (TextExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }
    }

    private static bool IsIgnoredMetadataFile(string relativePath)
    {
        if (relativePath.StartsWith(
                "_unifiedtoolkit_reports/model-selection/",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                "_unifiedtoolkit_reports/model-cleanup/",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // This command intentionally contains historical rejected paths solely
        // to generate the model-selection audit. They are not runtime references.
        return relativePath.Equals(
            "tools/UnifiedToolkit/Commands/GeneratePrototypeSaveCommand.cs",
            StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(
                "Commands/GeneratePrototypeSaveCommand.cs",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
