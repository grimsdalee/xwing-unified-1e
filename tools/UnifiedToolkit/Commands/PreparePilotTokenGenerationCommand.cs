using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class PreparePilotTokenGenerationCommand
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
            var repositoryRoot = args[0];
            string? output = null;
            for (var index = 1; index < args.Length; index++)
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

            Console.WriteLine("UnifiedToolkit Prepare Pilot Token Generation");
            Console.WriteLine("=============================================");
            Console.WriteLine();

            var result = new PilotTokenGenerationPreparationService().Prepare(repositoryRoot, output);
            Console.WriteLine($"Generation-required pilots: {result.PilotCount}");
            Console.WriteLine($"Pilots with donors:         {result.PilotsWithDonors}");
            Console.WriteLine($"Pilots without donors:      {result.PilotsWithoutDonors}");
            Console.WriteLine();
            Console.WriteLine($"Plan: {result.PlanFile}");
            Console.WriteLine($"HTML: {result.HtmlFile}");
            return result.PilotsWithoutDonors == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
        => Console.WriteLine("  prepare-pilot-token-generation <first-edition-repo-folder> [--output <folder>]");
}
