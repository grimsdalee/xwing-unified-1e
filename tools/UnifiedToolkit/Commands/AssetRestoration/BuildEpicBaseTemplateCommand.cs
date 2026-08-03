using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class BuildEpicBaseTemplateCommand
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
            var output = ReadOption(args, "--output");
            var result = EpicBaseTemplateBuilder.Build(
                repositoryRoot,
                output);

            Console.WriteLine(
                "UnifiedToolkit Phase 15A Epic Base Template Builder");
            Console.WriteLine(
                "====================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine(
                $"Implementation:         " +
                $"{result.Template.ImplementationVersion}");
            Console.WriteLine();
            Console.WriteLine(
                $"Canvas:                 " +
                $"{result.Template.Canvas.Width} x " +
                $"{result.Template.Canvas.Height}");
            Console.WriteLine(
                $"Sections:               " +
                $"{result.Template.Layout.Sections.Count}");
            Console.WriteLine(
                $"Ship mount markers:     " +
                $"{result.Template.Layout.ShipMountMarkers.Count}");
            Console.WriteLine(
                $"Common regions:         " +
                $"{result.Template.Layout.CommonRegions.Count}");
            Console.WriteLine(
                $"Layout status:          " +
                $"{result.Template.Layout.Status}");
            Console.WriteLine(
                $"Calibration status:     " +
                $"{result.Template.Layout.Calibration.Status}");
            Console.WriteLine(
                $"Warnings:               " +
                $"{result.Template.ValidationWarnings.Count}");
            Console.WriteLine();
            Console.WriteLine(
                $"Template:               {result.TemplatePath}");
            Console.WriteLine(
                $"Calibration overlay:    " +
                $"{result.CalibrationOverlayPath}");
            Console.WriteLine(
                $"Report:                 {result.ReportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "R5 calibrates reusable Epic base geometry only. " +
                "No ship-specific artwork was generated.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic base template generation failed: {ex.Message}");
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
            "Usage: UnifiedToolkit build-epic-base-template " +
            "<first-edition-repo-folder> [--output <file>]");
    }
}
