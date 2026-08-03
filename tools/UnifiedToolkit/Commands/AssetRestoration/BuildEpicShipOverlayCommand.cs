using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class BuildEpicShipOverlayCommand
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
            var shipId = ReadOption(args, "--ship")
                ?? throw new ArgumentException(
                    "--ship is required.");
            var output = ReadOption(args, "--output");
            var result = EpicShipOverlayBuilder.Build(
                repositoryRoot,
                shipId,
                output);

            Console.WriteLine(
                "UnifiedToolkit Phase 15A Epic Ship Overlay Builder");
            Console.WriteLine(
                "===================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine(
                $"Ship:                   {result.Overlay.ShipName} " +
                $"({result.Overlay.ShipId})");
            Console.WriteLine(
                $"Implementation:         " +
                $"{result.Overlay.ImplementationVersion}");
            Console.WriteLine();
            Console.WriteLine(
                $"Base template:          " +
                $"{result.Overlay.BaseTemplatePath}");
            Console.WriteLine(
                $"Ship-specific regions:  " +
                $"{result.Overlay.ShipRegions.Count}");
            Console.WriteLine(
                $"Reference photographs:  " +
                $"{result.Overlay.Photographs.Count}");
            Console.WriteLine(
                $"Fonts loaded:           " +
                $"{result.Overlay.Fonts.Count(font => font.LoadedSuccessfully)}/" +
                $"{result.Overlay.Fonts.Count}");
            Console.WriteLine(
                $"Warnings:               " +
                $"{result.Overlay.ValidationWarnings.Count}");
            Console.WriteLine();
            Console.WriteLine(
                $"Overlay:                {result.OverlayPath}");
            Console.WriteLine(
                $"Report:                 {result.ReportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "R4 defines ship-specific semantic content only. " +
                "No final artwork was generated.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic ship overlay generation failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ReadOption(
        string[] args,
        string name)
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
            "Usage: UnifiedToolkit build-epic-ship-overlay " +
            "<first-edition-repo-folder> --ship <ship-id> " +
            "[--output <file>]");
    }
}
