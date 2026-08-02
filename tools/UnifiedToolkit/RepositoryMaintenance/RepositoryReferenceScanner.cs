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
        "_unifiedtoolkit_quarantine",
        "_unifiedtoolkit_backups"
    };

    public IReadOnlyList<RepositoryReference> FindReferences(
        string repositoryRoot,
        string repositoryRelativePath)
    {
        var normalisedPath = Normalise(repositoryRelativePath);
        var fileName = Path.GetFileName(repositoryRelativePath);
        var results = new List<RepositoryReference>();

        foreach (var file in EnumerateCandidateFiles(repositoryRoot))
        {
            var relative = Normalise(Path.GetRelativePath(repositoryRoot, file));

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
            if (!normalisedContent.Contains(
                    normalisedPath,
                    StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(fileName)
                    || !normalisedContent.Contains(
                        fileName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(Classify(relative));
        }

        return results
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RepositoryReference Classify(string relativePath)
    {
        // Backups are retained rollback material, never active dependencies.
        // Keep this classification as a defensive second layer even though
        // backup directories are excluded during enumeration.
        if (relativePath.StartsWith(
                "_unifiedtoolkit_backups/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "Backup", blocksCleanup: false);
        }

        if (relativePath.StartsWith(
                "_unifiedtoolkit_reports/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "Report", blocksCleanup: false);
        }

        if (relativePath.StartsWith(
                "assets/manifests/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "Manifest", blocksCleanup: false);
        }

        if (relativePath.StartsWith(
                "ukb/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "KnowledgeBase", blocksCleanup: false);
        }

        if (relativePath.StartsWith(
                "assets/generated/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "Generated", blocksCleanup: false);
        }

        // These locations are retained Unified 2.5 conversion inputs and
        // extracted source snapshots. References here document the upstream
        // source state; they are not active Unified First Edition runtime
        // dependencies and must not block cleanup of imported model copies.
        if (relativePath.StartsWith(
                "assets/source/unified25/TTS_xwing/",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                "output/unified-2.5/",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                "source/unified-2.5/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "HistoricalUnified25Source", blocksCleanup: false);
        }

        // This command deliberately contains historical rejected paths so it
        // can generate the model-selection audit. Those strings are evidence,
        // not live runtime dependencies.
        if (relativePath.Equals(
                "tools/UnifiedToolkit/Commands/GeneratePrototypeSaveCommand.cs",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(
                "Commands/GeneratePrototypeSaveCommand.cs",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(
                "tools/UnifiedToolkit/Commands/RepositoryMaintenance/" +
                "MigrateShipModelPipelineReferencesCommand.cs",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(
                "Commands/RepositoryMaintenance/" +
                "MigrateShipModelPipelineReferencesCommand.cs",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "ModelSelectionAuditSource", blocksCleanup: false);
        }

        if (relativePath.StartsWith(
                "assets/source/",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "SourceAsset", blocksCleanup: true);
        }

        if (relativePath.StartsWith(
                "tools/",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                "Commands/",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(
                ".cs",
                StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(
                ".lua",
                StringComparison.OrdinalIgnoreCase))
        {
            return New(relativePath, "Runtime", blocksCleanup: true);
        }

        // Unknown locations remain blocking. The verifier should fail safe
        // rather than silently treating an unclassified reference as history.
        return New(relativePath, "Other", blocksCleanup: true);
    }

    private static RepositoryReference New(
        string path,
        string category,
        bool blocksCleanup) =>
        new()
        {
            Path = path,
            Category = category,
            BlocksCleanup = blocksCleanup
        };

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

    private static string Normalise(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
