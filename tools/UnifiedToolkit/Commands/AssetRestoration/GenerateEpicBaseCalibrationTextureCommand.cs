using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class GenerateEpicBaseCalibrationTextureCommand
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
            var result = EpicBaseCalibrationTextureGenerator.Generate(
                repositoryRoot,
                ReadOption(args, "--template"),
                ReadOption(args, "--output"));

            Console.WriteLine(
                "UnifiedToolkit Phase 15B Epic Base Calibration Texture");
            Console.WriteLine(
                "========================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Canvas:                 {result.Width} x {result.Height}");
            Console.WriteLine($"Template:               {result.TemplatePath}");
            Console.WriteLine($"Output:                 {result.OutputPath}");
            Console.WriteLine($"SHA-256:                {result.Sha256}");
            Console.WriteLine();
            Console.WriteLine(
                "Calibration geometry only. No final Epic artwork was generated.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic calibration texture generation failed: {ex.Message}");
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
            "Usage: UnifiedToolkit generate-epic-base-calibration-texture " +
            "<first-edition-repo-folder> " +
            "[--template <file>] [--output <file>]");
    }
}
