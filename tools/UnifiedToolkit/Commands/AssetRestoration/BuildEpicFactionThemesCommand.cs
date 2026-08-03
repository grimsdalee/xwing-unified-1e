using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class BuildEpicFactionThemesCommand
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
            var repositoryRoot = Path.GetFullPath(args[0]);
            var output = EpicFactionThemeWriter.Write(
                repositoryRoot,
                ReadOption(args, "--output"));

            Console.WriteLine(
                "UnifiedToolkit Phase 15C Epic Faction Themes");
            Console.WriteLine(
                "============================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine(
                $"Themes:                 " +
                $"{EpicFactionThemeCatalogue.All.Count}");
            Console.WriteLine($"Output:                 {output}");
            Console.WriteLine();
            Console.WriteLine(
                "Themes written for Rebel Alliance, " +
                "Galactic Empire, and Scum and Villainy.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic faction-theme generation failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (args[index].Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void ShowUsage()
    {
        Console.WriteLine(
            "Usage: UnifiedToolkit build-epic-faction-themes " +
            "<first-edition-repo-folder> [--output <file>]");
    }
}
