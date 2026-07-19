using UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

namespace UnifiedToolkit.Commands;

public static class PreparePilotTokenReviewCommand
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
            string? pilotLinks = null;
            string? output = null;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--pilot-links" when i + 1 < args.Length:
                        pilotLinks = args[++i];
                        break;
                    case "--output" when i + 1 < args.Length:
                        output = args[++i];
                        break;
                    default:
                        Console.WriteLine($"Unknown or incomplete option: {args[i]}");
                        ShowUsage();
                        return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Pilot Token Review Package");
            Console.WriteLine("=========================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}");
            Console.WriteLine();

            var result = new PilotTokenReviewPackageBuilder().Build(repositoryRoot, pilotLinks, output);

            Console.WriteLine($"Ambiguous sheet pilots: {result.ReviewPilots}");
            Console.WriteLine($"Missing sheet pilots:   {result.MissingSheetPilots}");
            Console.WriteLine($"Candidate images:       {result.CandidateImages}");
            Console.WriteLine();
            Console.WriteLine($"HTML review:       {result.HtmlFile}");
            Console.WriteLine($"Decision template: {result.DecisionTemplateFile}");
            Console.WriteLine();
            Console.WriteLine("Review package generated successfully. No asset decisions were applied.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage() => Console.WriteLine("  prepare-pilot-token-review <first-edition-repo-folder> [--pilot-links <pilot-links.json>] [--output <folder>]");
}
