using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class BuildEpicShipTargetingLayoutsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var root = Path.GetFullPath(args[0]);
            var output = EpicShipTargetingLayoutBuilder.Build(
                root, ReadOption(args, "--output"));
            Console.WriteLine(
                "UnifiedToolkit Phase 15C-R4 Epic Ship Targeting Layouts");
            Console.WriteLine(
                "====================================================");
            Console.WriteLine($"Repository:             {root}");
            Console.WriteLine("Ships:                  5");
            Console.WriteLine($"Output:                 {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic targeting-layout generation failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void ShowUsage() => Console.WriteLine(
        "Usage: UnifiedToolkit build-epic-ship-targeting-layouts " +
        "<first-edition-repo-folder> [--output <file>]");
}
