using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class GenerateEpicShipTargetingTextureCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var root = Path.GetFullPath(args[0]);
            var ship = ReadOption(args, "--ship")
                ?? throw new ArgumentException("--ship is required.");
            var result = EpicShipTargetingTextureGenerator.Generate(
                root, ship,
                ReadOption(args, "--catalogue"),
                ReadOption(args, "--template"),
                ReadOption(args, "--mount-points"),
                ReadOption(args, "--common-texture"),
                ReadOption(args, "--output"));

            Console.WriteLine(
                "UnifiedToolkit Phase 15C-R5 Epic Ship Targeting Texture");
            Console.WriteLine(
                "====================================================");
            Console.WriteLine($"Repository:             {root}");
            Console.WriteLine(
                $"Ship:                   {result.ShipName} ({result.ShipId})");
            Console.WriteLine(
                $"Targeting geometry:     {result.GeometryCount}");
            Console.WriteLine(
                $"Turret indicators:      {result.TurretIndicatorCount}");
            Console.WriteLine($"Output:                 {result.OutputPath}");
            Console.WriteLine($"SHA-256:                {result.Sha256}");
            Console.WriteLine($"Report:                 {result.ReportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic targeting-texture generation failed: {ex.Message}");
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
        "Usage: UnifiedToolkit generate-epic-ship-targeting-texture " +
        "<first-edition-repo-folder> --ship <ship-id> " +
        "[--catalogue <file>] [--template <file>] " +
        "[--mount-points <file>] [--common-texture <file>] " +
        "[--output <file>]");
}
