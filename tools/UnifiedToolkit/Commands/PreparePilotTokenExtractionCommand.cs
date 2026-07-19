using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class PreparePilotTokenExtractionCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repositoryRoot = args[0]; string? pilotLinks = null; string? output = null;
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--pilot-links" when i + 1 < args.Length: pilotLinks = args[++i]; break;
                    case "--output" when i + 1 < args.Length: output = args[++i]; break;
                    default: Console.WriteLine($"Unknown or incomplete option: {args[i]}"); ShowUsage(); return 1;
                }
            }
            Console.WriteLine("UnifiedToolkit Prepare Pilot Token Extraction");
            Console.WriteLine("=============================================");
            Console.WriteLine(); Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}"); Console.WriteLine();
            var result = new PilotTokenExtractionPreparationService().Prepare(repositoryRoot, pilotLinks, output);
            Console.WriteLine($"Approved source sheets: {result.Sheets}");
            Console.WriteLine($"Pilots to position:     {result.Entries}");
            Console.WriteLine($"Pilots without sheets:  {result.UnresolvedSheets}");
            Console.WriteLine(); Console.WriteLine($"HTML review: {result.HtmlFile}"); Console.WriteLine($"Plan template: {result.PlanFile}");
            Console.WriteLine(); Console.WriteLine("Extraction preparation generated successfully. No images were cropped.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"Error: {exception.Message}"); return 1; }
    }

    private static void ShowUsage() => Console.WriteLine("  prepare-pilot-token-extraction <first-edition-repo-folder> [--pilot-links <pilot-links.json>] [--output <folder>]");
}
