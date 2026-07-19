using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class InspectLegacyPilotSourceCommand
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
            string? legacySave = null;
            string? output = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (args[i].Equals("--legacy-save", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    legacySave = args[++i];
                else if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    output = args[++i];
                else
                {
                    Console.WriteLine($"Unknown or incomplete option: {args[i]}");
                    ShowUsage();
                    return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Inspect Legacy Pilot Source");
            Console.WriteLine("==========================================");
            Console.WriteLine();

            var result = new LegacyPilotSourceInspectionService().Inspect(args[0], args[1], legacySave, output);

            Console.WriteLine($"Pilot:             {result.PilotName}");
            Console.WriteLine($"Matching objects:  {result.MatchingObjects}");
            Console.WriteLine($"URL references:    {result.UrlReferences}");
            Console.WriteLine($"Unique URLs:        {result.UniqueUrls}");
            Console.WriteLine();
            Console.WriteLine($"Objects: {result.ObjectsJsonPath}");
            Console.WriteLine($"URLs:    {result.UrlsCsvPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("  inspect-legacy-pilot-source <first-edition-repo-folder> <pilot-name> [--legacy-save <save.json>] [--output <folder>]");
    }
}
