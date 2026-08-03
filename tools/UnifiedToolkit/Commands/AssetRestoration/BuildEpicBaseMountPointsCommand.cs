using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class BuildEpicBaseMountPointsCommand
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
            var spawnedSave = Path.GetFullPath(args[1]);
            var result = EpicBaseMountPointBuilder.Build(
                repositoryRoot,
                spawnedSave,
                ReadOption(args, "--output"));

            Console.WriteLine(
                "UnifiedToolkit Phase 15B Peg-Axis Epic Mount Points");
            Console.WriteLine(
                "======================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Spawned save:           {spawnedSave}");
            Console.WriteLine(
                $"Source base GUID:       " +
                $"{result.Database.SourceBaseGuid}");
            Console.WriteLine(
                $"Peg mesh:               " +
                $"{result.Database.PegMesh}");
            Console.WriteLine(
                $"Projection:             " +
                $"{result.Database.Projection.Method}");
            Console.WriteLine(
                $"Peg components:         " +
                $"{result.Database.Projection.PegConnectedComponents}");
            Console.WriteLine(
                $"Mount points:           " +
                $"{result.Database.MountPoints.Count}");

            foreach (var point in result.Database.MountPoints)
            {
                Console.WriteLine(
                    $"{point.Section,-23}" +
                    $"runtime Z {point.RuntimeLocalPosition.Z,9:F4}; " +
                    $"peg axis " +
                    $"({point.PegGeometryLocalPosition.X:F6}, " +
                    $"{point.PegGeometryLocalPosition.Z:F6}); " +
                    $"UV ({point.TextureUv.U:F6}, " +
                    $"{point.TextureUv.V:F6})");
            }

            Console.WriteLine($"Output:                 {result.OutputPath}");
            Console.WriteLine($"Report:                 {result.ReportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Marker positions were derived from each peg shaft axis and projected with exact barycentric UV interpolation.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic mount-point generation failed: {ex.Message}");
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
            "Usage: UnifiedToolkit build-epic-base-mount-points " +
            "<first-edition-repo-folder> <spawned-epic-save.json> " +
            "[--output <file>]");
    }
}
