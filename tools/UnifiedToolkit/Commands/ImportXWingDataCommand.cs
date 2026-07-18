using UnifiedToolkit.KnowledgeBase;
using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.Commands;

public static class ImportXWingDataCommand
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
            var xwingDataRoot = args[0];
            var firstEditionRepositoryRoot = args[1];
            var dryRun = false;
            var rebuildKnowledgeBase = true;

            for (var index = 2; index < args.Length; index++)
            {
                if (args[index].Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                    continue;
                }

                if (args[index].Equals("--no-rebuild-knowledge-base", StringComparison.OrdinalIgnoreCase))
                {
                    rebuildKnowledgeBase = false;
                    continue;
                }

                Console.WriteLine($"Unknown option: {args[index]}");
                ShowUsage();
                return 1;
            }

            Console.WriteLine("UnifiedToolkit xwing-data Import");
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine($"xwing-data repository:   {Path.GetFullPath(xwingDataRoot)}");
            Console.WriteLine($"First Edition repository: {Path.GetFullPath(firstEditionRepositoryRoot)}");
            Console.WriteLine($"Mode:                    {(dryRun ? "Dry run" : "Import")}");
            Console.WriteLine();

            var result = new XWingDataImporter().Import(xwingDataRoot, firstEditionRepositoryRoot, dryRun);

            Console.WriteLine($"Files discovered:     {result.FilesDiscovered}");
            Console.WriteLine($"Artwork files:        {result.AssetFiles}");
            Console.WriteLine($"Reference-data files: {result.ReferenceDataFiles}");
            Console.WriteLine($"Schema files:         {result.SchemaFiles}");
            Console.WriteLine($"Copied:               {result.FilesCopied}");
            Console.WriteLine($"Updated:              {result.FilesUpdated}");
            Console.WriteLine($"Unchanged:            {result.FilesUnchanged}");
            Console.WriteLine($"Failed:               {result.FilesFailed}");
            Console.WriteLine($"Bytes selected:       {result.BytesSelected:N0}");
            Console.WriteLine();
            Console.WriteLine($"Manifest: {result.ManifestPath}");
            Console.WriteLine($"Report:   {result.ReportPath}");

            if (!dryRun && result.FilesFailed == 0 && rebuildKnowledgeBase)
            {
                Console.WriteLine();
                Console.WriteLine("Refreshing asset catalogue and Unified Knowledge Base...");
                var build = new KnowledgeBaseBuilder().Build(firstEditionRepositoryRoot, refreshCatalogue: true);
                Console.WriteLine($"Asset files:       {build.FileCount}");
                Console.WriteLine($"Unique assets:     {build.UniqueAssetCount}");
                Console.WriteLine($"Validation errors: {build.ErrorCount}");
                Console.WriteLine($"Knowledge base:    {build.OutputRoot}");
                if (build.ErrorCount > 0)
                    return 2;
            }

            Console.WriteLine();
            Console.WriteLine(result.FilesFailed == 0
                ? dryRun
                    ? "Dry run completed successfully. No source files were copied."
                    : "xwing-data import completed successfully."
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
        Console.WriteLine("  import-xwing-data <xwing-data-folder> <first-edition-repo-folder> [--dry-run] [--no-rebuild-knowledge-base]");
    }
}
