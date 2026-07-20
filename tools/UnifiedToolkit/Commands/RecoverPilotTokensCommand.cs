using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class RecoverPilotTokensCommand
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
            var recoveryPlan = args[1];
            string? output = null;
            var overwrite = false;

            for (var index = 2; index < args.Length; index++)
            {
                switch (args[index].ToLowerInvariant())
                {
                    case "--output" when index + 1 < args.Length:
                        output = args[++index];
                        break;
                    case "--overwrite":
                        overwrite = true;
                        break;
                    default:
                        Console.WriteLine($"Unknown or incomplete option: {args[index]}");
                        ShowUsage();
                        return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Recover Pilot Tokens");
            Console.WriteLine("==================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}");
            Console.WriteLine($"Plan:       {Path.GetFullPath(recoveryPlan)}");
            Console.WriteLine($"Overwrite:  {overwrite}");
            Console.WriteLine();

            var result = new PilotTokenRecoveryService().Recover(repositoryRoot, recoveryPlan, output, overwrite);
            Console.WriteLine($"Recovery assignments:       {result.AssignmentsInPlan}");
            Console.WriteLine($"Tokens recovered:           {result.RecoveredTokens}");
            Console.WriteLine($"Existing tokens skipped:    {result.ExistingTokensSkipped}");
            Console.WriteLine($"Recovery failures:          {result.FailedTokens}");
            Console.WriteLine($"Generation required pilots: {result.GenerationRequiredPilots}");
            Console.WriteLine();
            Console.WriteLine($"Output:              {result.OutputFolder}");
            Console.WriteLine($"Manifest:            {result.ManifestFile}");
            Console.WriteLine($"Report:              {result.ReportFile}");
            Console.WriteLine($"Generation required: {result.GenerationRequiredFile}");
            Console.WriteLine();
            Console.WriteLine(result.FailedTokens == 0
                ? "Recovered pilot tokens were cropped successfully. Generation-required pilots were preserved for the next phase."
                : "Recovery completed with failures. Review the CSV report before proceeding.");
            return result.FailedTokens == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
        => Console.WriteLine("  recover-pilot-tokens <first-edition-repo-folder> <completed-recovery-plan.json> [--output <folder>] [--overwrite]");
}
