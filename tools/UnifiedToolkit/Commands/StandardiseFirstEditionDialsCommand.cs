using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class StandardiseFirstEditionDialsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && !string.Equals(args[1], "--inventory-only", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  standardise-first-edition-dials <first-edition-repo-folder> [--inventory-only]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var inventoryOnly = args.Any(value => string.Equals(value, "--inventory-only", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("UnifiedToolkit Phase 10G First Edition Dial Standardisation");
            Console.WriteLine("============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:       {repositoryRoot}");
            Console.WriteLine($"Mode:             {(inventoryOnly ? "Inventory only" : "Inventory and standardise")}");
            Console.WriteLine("Target format:    PNG");
            Console.WriteLine("Target dimensions: 250 x 250 pixels");
            Console.WriteLine();

            var result = new FirstEditionDialStandardisationService().Run(repositoryRoot, inventoryOnly);

            Console.WriteLine($"Images scanned:             {result.ImagesScanned}");
            Console.WriteLine($"Already compliant:          {result.AlreadyCompliant}");
            Console.WriteLine($"Format conversion required: {result.FormatConversionRequired}");
            Console.WriteLine($"Resize required:            {result.ResizeRequired}");
            Console.WriteLine($"Generated:                  {result.Generated}");
            Console.WriteLine($"Unchanged outputs:          {result.UnchangedOutputs}");
            Console.WriteLine($"Warnings:                   {result.Warnings}");
            Console.WriteLine($"Errors:                     {result.Errors}");
            Console.WriteLine();
            Console.WriteLine($"Inventory:                  {result.InventoryCsv}");
            Console.WriteLine($"Report:                     {result.ReportFile}");

            if (!inventoryOnly)
            {
                Console.WriteLine($"Generated assets:           {result.GeneratedRoot}");
                Console.WriteLine($"Manifest:                   {result.ManifestFile}");
            }

            Console.WriteLine();
            Console.WriteLine(result.Errors == 0
                ? inventoryOnly
                    ? "Dial inventory completed successfully. No images were changed."
                    : "First Edition dials standardised successfully. Curated source images were not modified."
                : "Dial standardisation completed with errors.");

            return result.Errors == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
