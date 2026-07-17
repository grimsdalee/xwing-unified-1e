using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.Commands;

public static class CatalogueRepositoryAssetsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  catalogue-repository-assets <first-edition-repo-folder> [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = args[0];
            string? outputFolder = null;

            for (var index = 1; index < args.Length; index++)
            {
                if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.WriteLine("Missing value after --output.");
                        return 1;
                    }

                    outputFolder = args[++index];
                    continue;
                }

                Console.WriteLine($"Unknown option: {args[index]}");
                return 1;
            }

            Console.WriteLine("UnifiedToolkit Repository Asset Catalogue");
            Console.WriteLine("==========================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}");
            Console.WriteLine();

            var builder = new AssetRepositoryCatalogueBuilder();
            var result = builder.Build(repositoryRoot, outputFolder);

            Console.WriteLine($"Source files:    {result.SourceFiles}");
            Console.WriteLine($"Generated files: {result.GeneratedFiles}");
            Console.WriteLine($"Total files:     {result.TotalFiles}");
            Console.WriteLine($"Unique assets:   {result.UniqueAssets}");
            Console.WriteLine($"Duplicate files: {result.DuplicateFiles}");
            Console.WriteLine();
            Console.WriteLine($"Manifests: {result.ManifestRoot}");
            Console.WriteLine();
            Console.WriteLine("Catalogue completed successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
