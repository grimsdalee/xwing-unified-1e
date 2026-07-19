using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class ExtractPilotTokensCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { ShowUsage(); return 1; }
        try
        {
            var repositoryRoot = args[0];
            var planFile = args[1];
            string? output = null;
            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--output" when i + 1 < args.Length: output = args[++i]; break;
                    default: Console.WriteLine($"Unknown or incomplete option: {args[i]}"); ShowUsage(); return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Extract Pilot Tokens");
            Console.WriteLine("==================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}");
            Console.WriteLine($"Plan:       {Path.GetFullPath(planFile)}");
            Console.WriteLine();

            var result = new PilotTokenExtractionService().Extract(repositoryRoot, planFile, output);
            Console.WriteLine($"Sheets in plan:             {result.SheetsInPlan}");
            Console.WriteLine($"Complete sheets processed:  {result.CompleteSheets}");
            Console.WriteLine($"Incomplete sheets skipped:  {result.SkippedIncompleteSheets}");
            Console.WriteLine($"Tokens generated:           {result.GeneratedTokens}");
            Console.WriteLine($"Token failures:             {result.FailedTokens}");
            Console.WriteLine();
            Console.WriteLine($"Output:   {result.OutputFolder}");
            Console.WriteLine($"Manifest: {result.ManifestFile}");
            Console.WriteLine($"Report:   {result.ReportFile}");
            Console.WriteLine();
            Console.WriteLine(result.FailedTokens == 0
                ? "Completed pilot tokens were extracted successfully. Incomplete sheets were left for later."
                : "Extraction completed with failures. Review the CSV report before proceeding.");
            return result.FailedTokens == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage() => Console.WriteLine("  extract-pilot-tokens <first-edition-repo-folder> <completed-plan.json> [--output <folder>]");
}
