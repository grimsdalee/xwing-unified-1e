using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.Commands;

public static class ImportUnifiedAssetsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var unifiedRepositoryRoot = args[0];
            var firstEditionRepositoryRoot = args[1];
            var dryRun = false;
            string? outputFolder = null;

            for (var index = 2; index < args.Length; index++)
            {
                if (args[index].Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                    continue;
                }

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
                ShowUsage();
                return 1;
            }

            Console.WriteLine("UnifiedToolkit Unified 2.5 Asset Import");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Unified repository:     {Path.GetFullPath(unifiedRepositoryRoot)}");
            Console.WriteLine($"First Edition repository: {Path.GetFullPath(firstEditionRepositoryRoot)}");
            Console.WriteLine($"Mode:                   {(dryRun ? "Dry run" : "Import")}");
            Console.WriteLine();

            var importer = new UnifiedAssetImporter();
            var result = importer.Import(
                unifiedRepositoryRoot,
                firstEditionRepositoryRoot,
                outputFolder,
                dryRun);

            Console.WriteLine($"Files discovered: {result.FilesDiscovered}");
            Console.WriteLine($"Files selected:   {result.FilesSelected}");
            Console.WriteLine($"Copied:           {result.FilesCopied}");
            Console.WriteLine($"Updated:          {result.FilesUpdated}");
            Console.WriteLine($"Unchanged:        {result.FilesUnchanged}");
            Console.WriteLine($"Skipped:          {result.FilesSkipped}");
            Console.WriteLine($"Failed:           {result.FilesFailed}");
            Console.WriteLine($"Bytes selected:   {result.BytesSelected:N0}");
            Console.WriteLine();
            Console.WriteLine($"Destination: {result.DestinationRoot}");
            Console.WriteLine($"Report:      {result.ReportPath}");
            Console.WriteLine($"Manifest:    {result.ManifestPath}");

            if (!dryRun)
            {
                Console.WriteLine($"Catalogue:   {result.CatalogueManifestRoot}");
            }

            Console.WriteLine();
            Console.WriteLine(result.FilesFailed == 0
                ? dryRun
                    ? "Dry run completed successfully. No files were changed."
                    : "Unified asset import completed successfully."
                : "Import completed with failures. Review the report before continuing.");

            return result.FilesFailed == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  import-unified-assets <unified-repo-folder> <first-edition-repo-folder> [--dry-run] [--output <folder>]");
    }
}
