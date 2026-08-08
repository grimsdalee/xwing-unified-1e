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
            var compareSizes = HasFlag(args, "--compare-sizes");
            var manifest = EpicBaseValidationSaveGenerator.Generate(
                repositoryRoot,
                referenceSave,
                ReadOption(args, "--template"),
                ReadOption(args, "--texture"),
                ReadOption(args, "--output"),
                ReadOption(args, "--asset-base-url"),
                ReadOption(args, "--ship"),
                compareSizes);

            var ship = ReadOption(args, "--ship");

            Console.WriteLine(
                compareSizes
                    ? "UnifiedToolkit Phase 15C Epic Base Size Comparison Save"
                    : ship is null
                    ? "UnifiedToolkit Phase 15B Epic Base Validation Save"
                    : "UnifiedToolkit Phase 15C Locked Epic Ship Base Validation Save");
            Console.WriteLine(
                "==================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Reference save:         {referenceSave}");
            Console.WriteLine($"Base mesh:              {manifest.BaseMesh}");
            Console.WriteLine(
                ship is null
                    ? $"Calibration texture:    {manifest.CalibrationTexture}"
                    : $"Locked texture:         {manifest.CalibrationTexture}");
            if (ship is not null)
                Console.WriteLine($"Locked ship:            {ship}");
            if (compareSizes)
                Console.WriteLine("Base sizes:             Epic Long + Epic Short");
            Console.WriteLine($"Objects:                {manifest.ObjectCount}");
            Console.WriteLine($"Save:                   {manifest.SavePath}");
            Console.WriteLine();
            Console.WriteLine(
                compareSizes
                    ? "Load the generated save in Tabletop Simulator and compare the long and short First Edition Epic footprints and peg spacing."
                    : ship is null
                    ? "Load the generated save in Tabletop Simulator and verify orientation, divider, mount markers and UV coverage."
                    : "Load the generated save in Tabletop Simulator and verify the locked ship-base artwork and UV coverage.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic base validation save generation failed: {ex.Message}");
            return 1;
        }
    }

    private static bool HasFlag(
        string[] args,
        string name) =>
        args.Skip(2).Any(
            value => value.Equals(
                name,
                StringComparison.OrdinalIgnoreCase));

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
            "[--ship <ship-id>] [--compare-sizes] " +
            "[--template <file>] [--texture <file>] " +
            "[--output <file>] [--asset-base-url <url>]");
    }
}
