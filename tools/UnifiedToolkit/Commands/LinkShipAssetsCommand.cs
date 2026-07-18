using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.Commands;

public static class LinkShipAssetsCommand
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
            string? shipsFile = null;
            string? outputFolder = null;
            var limit = 8;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--ships" when i + 1 < args.Length: shipsFile = args[++i]; break;
                    case "--output" when i + 1 < args.Length: outputFolder = args[++i]; break;
                    case "--candidates" when i + 1 < args.Length && int.TryParse(args[++i], out var parsed): limit = Math.Clamp(parsed, 1, 50); break;
                    default:
                        Console.WriteLine($"Unknown or incomplete option: {args[i]}");
                        ShowUsage();
                        return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Ship Asset Linking");
            Console.WriteLine("=================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {Path.GetFullPath(repositoryRoot)}");
            Console.WriteLine($"Candidates per role: {limit}");
            Console.WriteLine();

            var result = new ShipAssetLinker().Link(repositoryRoot, shipsFile, outputFolder, limit);
            Console.WriteLine($"Ships processed:        {result.Ships}");
            Console.WriteLine($"Candidate links:        {result.CandidateLinks}");
            Console.WriteLine($"Clear role matches:     {result.ClearRoles}");
            Console.WriteLine($"Roles requiring review: {result.ReviewRoles}");
            Console.WriteLine($"Missing required roles: {result.MissingRequiredRoles}");
            Console.WriteLine();
            Console.WriteLine($"Output: {result.OutputRoot}");
            Console.WriteLine();
            Console.WriteLine("Candidate links generated successfully. No assets were automatically approved.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage() => Console.WriteLine("  link-ship-assets <first-edition-repo-folder> [--ships <ships.json>] [--candidates <1-50>] [--output <folder>]");
}
