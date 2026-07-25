namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

/// <summary>
/// Resolves the imported xwing-data snapshot.
///
/// Repository-local layout:
///   source/xwing-data/data
///   assets/source/xwing-data/images
///
/// An external xwing-data checkout remains supported when explicitly supplied:
///   <external>/data
///   <external>/images
/// </summary>
public static class FirstEditionDataSourceResolver
{
    public static FirstEditionDataSourceLayout Resolve(
        string repositoryRoot,
        string? explicitPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var explicitRoot = Path.GetFullPath(explicitPath);

            // Support accidentally passing the repository artwork root.
            if (IsRepositoryArtworkRoot(explicitRoot))
            {
                var inferredRepositoryRoot = InferRepositoryRootFromArtworkRoot(explicitRoot);
                return ResolveRepositoryLocal(inferredRepositoryRoot);
            }

            var externalLayout = new FirstEditionDataSourceLayout
            {
                DataRoot = explicitRoot,
                ImagesRoot = Path.Combine(explicitRoot, "images"),
                IsRepositoryLocalSplitLayout = false
            };

            Validate(externalLayout);
            return externalLayout;
        }

        return ResolveRepositoryLocal(repositoryRoot);
    }

    public static bool LooksLikeDataSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return Directory.Exists(Path.Combine(path, "data"))
            || IsRepositoryArtworkRoot(Path.GetFullPath(path));
    }

    private static FirstEditionDataSourceLayout ResolveRepositoryLocal(
        string repositoryRoot)
    {
        var layout = new FirstEditionDataSourceLayout
        {
            DataRoot = Path.Combine(repositoryRoot, "source", "xwing-data"),
            ImagesRoot = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "xwing-data",
                "images"),
            IsRepositoryLocalSplitLayout = true
        };

        Validate(layout);
        return layout;
    }

    private static bool IsRepositoryArtworkRoot(string path)
    {
        var normalised = path
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Directory.Exists(Path.Combine(normalised, "images"))
            && string.Equals(
                Path.GetFileName(normalised),
                "xwing-data",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetFileName(Path.GetDirectoryName(normalised)),
                "source",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetFileName(Path.GetDirectoryName(
                    Path.GetDirectoryName(normalised))),
                "assets",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string InferRepositoryRootFromArtworkRoot(string artworkRoot)
    {
        var sourceFolder = Directory.GetParent(artworkRoot)
            ?? throw new InvalidOperationException(
                $"Could not infer repository root from: {artworkRoot}");

        var assetsFolder = sourceFolder.Parent
            ?? throw new InvalidOperationException(
                $"Could not infer repository root from: {artworkRoot}");

        var repositoryRoot = assetsFolder.Parent
            ?? throw new InvalidOperationException(
                $"Could not infer repository root from: {artworkRoot}");

        return repositoryRoot.FullName;
    }

    private static void Validate(FirstEditionDataSourceLayout layout)
    {
        if (!Directory.Exists(layout.DataRoot))
            throw new DirectoryNotFoundException(
                $"xwing-data source root not found: {layout.DataRoot}");

        var dataFolder = Path.Combine(layout.DataRoot, "data");
        if (!Directory.Exists(dataFolder))
            throw new DirectoryNotFoundException(
                $"xwing-data data folder not found: {dataFolder}");

        if (!Directory.Exists(layout.ImagesRoot))
            throw new DirectoryNotFoundException(
                $"xwing-data images folder not found: {layout.ImagesRoot}");
    }
}

public sealed class FirstEditionDataSourceLayout
{
    public string DataRoot { get; init; } = string.Empty;
    public string ImagesRoot { get; init; } = string.Empty;
    public bool IsRepositoryLocalSplitLayout { get; init; }
}
