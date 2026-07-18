using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.Commands;

public static class ImportLegacyFirstEditionAssetsCommand
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
            var legacySavePath = args[0];
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

            Console.WriteLine("UnifiedToolkit Legacy First Edition Asset Import");
            Console.WriteLine("================================================");
            Console.WriteLine();
            Console.WriteLine($"Legacy save:             {Path.GetFullPath(legacySavePath)}");
            Console.WriteLine($"First Edition repository: {Path.GetFullPath(firstEditionRepositoryRoot)}");
            Console.WriteLine($"Mode:                    {(dryRun ? "Dry run" : "Import")}");
            Console.WriteLine();

            var importer = new LegacyFirstEditionAssetImporter();
            var result = importer.ImportAsync(
                    legacySavePath,
                    firstEditionRepositoryRoot,
                    outputFolder,
                    dryRun)
                .GetAwaiter()
                .GetResult();

            Console.WriteLine($"URL references:     {result.ReferenceCount}");
            Console.WriteLine($"Unique URLs:        {result.UniqueUrlCount}");
            Console.WriteLine($"Downloaded:         {result.DownloadedCount}");
            Console.WriteLine($"Unchanged:          {result.UnchangedCount}");
            Console.WriteLine($"Content duplicates: {result.DuplicateCount}");
            Console.WriteLine($"Skipped:            {result.SkippedCount}");
            Console.WriteLine($"Failed:             {result.FailedCount}");
            Console.WriteLine($"Bytes downloaded:   {result.BytesDownloaded:N0}");
            Console.WriteLine();
            Console.WriteLine($"Destination: {result.DestinationRoot}");
            Console.WriteLine($"Report:      {result.ReportPath}");
            Console.WriteLine($"Manifest:    {result.ManifestPath}");

            if (!dryRun)
            {
                Console.WriteLine($"Catalogue:   {result.CatalogueManifestRoot}");
            }

            Console.WriteLine();
            Console.WriteLine(result.FailedCount == 0
                ? dryRun
                    ? "Dry run completed successfully. No assets were downloaded."
                    : "Legacy First Edition asset import completed successfully."
                : "Import completed with failures. Review the report before continuing.");

            return result.FailedCount == 0 ? 0 : 1;
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
        Console.WriteLine("  import-legacy-first-edition-assets <legacy-save.json> <first-edition-repo-folder> [--dry-run] [--output <folder>]");
    }
}
