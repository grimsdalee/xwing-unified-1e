using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class ImportGeneratedPilotTokensCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  import-generated-pilot-tokens <first-edition-repo-folder>");
            return 1;
        }

        try
        {
            Console.WriteLine("UnifiedToolkit Generated Pilot Token Import");
            Console.WriteLine("============================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(args[0])}");
            Console.WriteLine("Image processing: None (files are copied byte-for-byte)");
            Console.WriteLine();

            var result = new GeneratedPilotTokenImportService().Import(args[0]);

            Console.WriteLine($"Images scanned: {result.ImagesScanned}");
            Console.WriteLine($"Imported:       {result.Imported}");
            Console.WriteLine($"Updated:        {result.Updated}");
            Console.WriteLine($"Warnings:       {result.Warnings}");
            Console.WriteLine($"Errors:         {result.Errors}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:       {result.ManifestFile}");
            Console.WriteLine($"Report:         {result.ReportFile}");
            Console.WriteLine($"Asset register: {result.AssetManifestRoot}");
            Console.WriteLine($"Knowledge base: {result.KnowledgeBaseRoot}");
            Console.WriteLine();
            Console.WriteLine(result.Errors == 0
                ? "Generated pilot tokens imported successfully."
                : "Generated pilot token import completed with errors.");

            return result.Errors == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
