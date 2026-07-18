using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.Commands;

public static class BuildKnowledgeBaseCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  build-knowledge-base <first-edition-repo-folder> [--output <folder>] [--no-refresh-catalogue]");
            return 1;
        }

        try
        {
            var repositoryRoot = args[0];
            string? outputFolder = null;
            var refreshCatalogue = true;

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

                if (args[index].Equals("--no-refresh-catalogue", StringComparison.OrdinalIgnoreCase))
                {
                    refreshCatalogue = false;
                    continue;
                }

                Console.WriteLine($"Unknown option: {args[index]}");
                return 1;
            }

            Console.WriteLine("UnifiedToolkit Unified Knowledge Base");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}");
            Console.WriteLine($"Catalogue refresh: {(refreshCatalogue ? "Yes" : "No")}");
            Console.WriteLine();

            var result = new KnowledgeBaseBuilder().Build(repositoryRoot, outputFolder, refreshCatalogue);

            Console.WriteLine($"Asset files:          {result.FileCount}");
            Console.WriteLine($"Unique assets:        {result.UniqueAssetCount}");
            Console.WriteLine($"Duplicate files:      {result.DuplicateFileCount}");
            Console.WriteLine($"Unavailable sources:  {result.UnavailableSourceCount}");
            Console.WriteLine($"Validation errors:    {result.ErrorCount}");
            Console.WriteLine($"Validation warnings:  {result.WarningCount}");
            Console.WriteLine();
            Console.WriteLine($"Knowledge base: {result.OutputRoot}");
            Console.WriteLine();
            Console.WriteLine(result.ErrorCount == 0
                ? "Knowledge base built successfully."
                : "Knowledge base built with validation errors.");

            return result.ErrorCount == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
