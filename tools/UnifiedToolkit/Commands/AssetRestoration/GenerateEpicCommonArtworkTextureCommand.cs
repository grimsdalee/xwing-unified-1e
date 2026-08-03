using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class GenerateEpicCommonArtworkTextureCommand
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
            var result = EpicCommonArtworkTextureGenerator.Generate(
                repositoryRoot,
                ReadOption(args, "--template"),
                ReadOption(args, "--mount-points"),
                ReadOption(args, "--output"));

            Console.WriteLine(
                "UnifiedToolkit Phase 15C Epic Common Artwork R2");
            Console.WriteLine(
                "=============================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine(
                $"Implementation:         " +
                $"{result.ImplementationVersion}");
            Console.WriteLine(
                $"Canvas:                 " +
                $"{result.Width} x {result.Height}");
            Console.WriteLine(
                $"Stars:                  {result.StarCount}");
            Console.WriteLine(
                $"Template:               {result.TemplatePath}");
            Console.WriteLine(
                $"Mount points:           " +
                $"{result.MountPointDatabasePath}");
            Console.WriteLine(
                $"Output:                 {result.OutputPath}");
            Console.WriteLine(
                $"SHA-256:                {result.Sha256}");
            Console.WriteLine(
                $"Report:                 {result.ReportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Generated shared Epic artwork only. " +
                "No ship-specific CR90 content was added.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic common artwork generation failed: {ex.Message}");
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
            "Usage: UnifiedToolkit generate-epic-common-artwork-texture " +
            "<first-edition-repo-folder> " +
            "[--template <file>] [--mount-points <file>] " +
            "[--output <file>]");
    }
}
