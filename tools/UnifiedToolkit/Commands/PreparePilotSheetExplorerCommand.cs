using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class PreparePilotSheetExplorerCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            string? inventory = null;
            string? plan = null;
            string? output = null;

            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--inventory", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    inventory = args[++i];
                else if (args[i].Equals("--plan", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    plan = args[++i];
                else if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    output = args[++i];
                else
                {
                    Console.WriteLine($"Unknown or incomplete option: {args[i]}");
                    ShowUsage();
                    return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Pilot Sheet Explorer");
            Console.WriteLine("===================================");
            Console.WriteLine();

            var result = new PilotSheetExplorerService().Prepare(args[0], inventory, plan, output);

            Console.WriteLine($"Candidate images indexed: {result.CandidateImages}");
            Console.WriteLine($"Known source sheets:      {result.KnownSourceSheets}");
            Console.WriteLine($"Known pilot crops:        {result.KnownPilotCrops}");
            Console.WriteLine($"Missing pilots loaded:    {result.MissingPilots}");
            Console.WriteLine($"Duplicate images skipped: {result.DuplicateImagesSkipped}");
            Console.WriteLine();
            Console.WriteLine($"HTML:      {result.HtmlFile}");
            Console.WriteLine($"Catalogue: {result.CatalogueFile}");
            Console.WriteLine();
            Console.WriteLine("Open index.html in a browser. Assign missing pilots by dragging a rectangle over a token, then download the recovery plan.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage() => Console.WriteLine(
        "  prepare-pilot-sheet-explorer <first-edition-repo-folder> [--inventory <pilot-token-inventory.csv>] [--plan <completed-extraction-plan.json>] [--output <folder>]");
}
