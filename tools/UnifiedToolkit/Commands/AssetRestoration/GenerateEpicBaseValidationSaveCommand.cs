using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class GenerateEpicBaseValidationSaveCommand
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
            var repositoryRoot = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            var manifest = EpicBaseValidationSaveGenerator.Generate(
                repositoryRoot,
                referenceSave,
                ReadOption(args, "--template"),
                ReadOption(args, "--texture"),
                ReadOption(args, "--output"),
                ReadOption(args, "--asset-base-url"));

            Console.WriteLine(
                "UnifiedToolkit Phase 15B Epic Base Validation Save");
            Console.WriteLine(
                "==================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Reference save:         {referenceSave}");
            Console.WriteLine($"Base mesh:              {manifest.BaseMesh}");
            Console.WriteLine(
                $"Calibration texture:    {manifest.CalibrationTexture}");
            Console.WriteLine($"Objects:                {manifest.ObjectCount}");
            Console.WriteLine($"Save:                   {manifest.SavePath}");
            Console.WriteLine();
            Console.WriteLine(
                "Load the generated save in Tabletop Simulator and verify " +
                "orientation, divider, mount markers and UV coverage.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic base validation save generation failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ReadOption(
        string[] args,
        string name)
    {
        for (var index = 2; index < args.Length - 1; index++)
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
            "Usage: UnifiedToolkit generate-epic-base-validation-save " +
            "<first-edition-repo-folder> <reference-save.json> " +
            "[--template <file>] [--texture <file>] " +
            "[--output <file>] [--asset-base-url <url>]");
    }
}
