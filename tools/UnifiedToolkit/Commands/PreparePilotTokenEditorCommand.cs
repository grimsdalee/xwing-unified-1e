using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class PreparePilotTokenEditorCommand
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
            var repositoryRoot = args[0];
            var completedPlan = args[1];
            string? output = null;

            for (var index = 2; index < args.Length; index++)
            {
                switch (args[index].ToLowerInvariant())
                {
                    case "--output" when index + 1 < args.Length:
                        output = args[++index];
                        break;
                    default:
                        Console.WriteLine($"Unknown or incomplete option: {args[index]}");
                        ShowUsage();
                        return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Prepare Pilot Token Editor");
            Console.WriteLine("=========================================");
            Console.WriteLine();

            var result = new PilotTokenEditorPreparationService().Prepare(
                repositoryRoot,
                completedPlan,
                output);

            Console.WriteLine($"Approved pilots:       {result.PilotCount}");
            Console.WriteLine($"Donors copied:         {result.DonorCount}");
            Console.WriteLine($"Pilot cards copied:    {result.PilotCardCount}");
            Console.WriteLine();
            Console.WriteLine($"Plan: {result.PlanFile}");
            Console.WriteLine($"HTML: {result.HtmlFile}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
        => Console.WriteLine(
            "  prepare-pilot-token-editor <first-edition-repo-folder> <completed-generation-plan.json> [--output <folder>]");
}
