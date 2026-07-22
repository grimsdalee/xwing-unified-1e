using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class AuditPilotTokenImagesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            string? output = null;
            for (var index = 1; index < args.Length; index++)
            {
                if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length) output = args[++index];
                else { Console.WriteLine($"Unknown or incomplete option: {args[index]}"); ShowUsage(); return 1; }
            }

            Console.WriteLine("UnifiedToolkit Pilot Token Image Audit");
            Console.WriteLine("======================================");
            Console.WriteLine();
            var result = new PilotTokenImageAuditService().Audit(args[0], output);
            Console.WriteLine($"Images scanned:             {result.ImagesScanned}");
            Console.WriteLine($"Unique canvas sizes:        {result.UniqueCanvasSizes}");
            Console.WriteLine($"Images with warnings:       {result.ImagesWithWarnings}");
            Console.WriteLine($"Exact duplicate groups:     {result.ExactDuplicateGroups}");
            Console.WriteLine($"Recommended small canvas:   {result.RecommendedSmallCanvas}");
            Console.WriteLine($"Recommended large canvas:   {result.RecommendedLargeCanvas}");
            Console.WriteLine();
            Console.WriteLine($"Audit:   {result.AuditCsv}");
            Console.WriteLine($"Summary: {result.SummaryFile}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage() => Console.WriteLine("  audit-pilot-token-images <first-edition-repo-folder> [--output <folder>]");
}
