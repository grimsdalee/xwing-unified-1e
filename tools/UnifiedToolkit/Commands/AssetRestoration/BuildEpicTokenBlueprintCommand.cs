using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class BuildEpicTokenBlueprintCommand
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
            var shipId = ReadOption(args, "--ship") ?? "cr90corvette";
            var output = ReadOption(args, "--output");
            var result = EpicTokenBlueprintBuilder.Build(
                repositoryRoot,
                shipId,
                output);

            var blueprint = result.Blueprint;
            Console.WriteLine(
                "UnifiedToolkit Phase 15A Epic Token Blueprint Builder");
            Console.WriteLine(
                "========================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine(
                $"Ship:                   {blueprint.ShipName} " +
                $"({blueprint.ShipId})");
            Console.WriteLine(
                $"Implementation:         {blueprint.ImplementationVersion}");
            Console.WriteLine();
            Console.WriteLine(
                $"Canvas:                 {blueprint.Canvas.Width} x " +
                $"{blueprint.Canvas.Height}");
            Console.WriteLine(
                $"Canvas shape:           {blueprint.Canvas.Shape}");
            Console.WriteLine(
                $"Texture mode:           {blueprint.Canvas.TextureMode}");
            Console.WriteLine(
                $"OBJ vertices:           {blueprint.Mesh.VertexCount}");
            Console.WriteLine(
                $"OBJ UV coordinates:     " +
                $"{blueprint.Mesh.TextureCoordinateCount}");
            Console.WriteLine(
                $"OBJ faces:              {blueprint.Mesh.FaceCount}");
            Console.WriteLine(
                $"Textured faces:         " +
                $"{blueprint.Mesh.FacesWithTextureCoordinates}");
            Console.WriteLine(
                $"Fonts loaded:           " +
                $"{blueprint.Fonts.Count(font => font.LoadedSuccessfully)}/" +
                $"{blueprint.Fonts.Count}");
            Console.WriteLine(
                $"Reference photographs:  {blueprint.Photographs.Count}");
            Console.WriteLine(
                $"Ship mount markers:     " +
                $"{blueprint.Layout.ShipMountMarkers.Count}");
            Console.WriteLine(
                $"Artwork regions:        " +
                $"{blueprint.Layout.ArtworkRegions.Count}");
            Console.WriteLine(
                $"Layout status:          {blueprint.Layout.Status}");
            Console.WriteLine(
                $"Warnings:               " +
                $"{blueprint.ValidationWarnings.Count}");
            Console.WriteLine();
            Console.WriteLine(
                $"Blueprint:              {result.BlueprintPath}");
            Console.WriteLine(
                $"Report:                 {result.ReportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "R3 defines a rectangular UV texture blueprint. It does not " +
                "reproduce physical cardboard geometry or generate artwork.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Epic token blueprint generation failed: {ex.Message}");
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
            "Usage: UnifiedToolkit build-epic-token-blueprint " +
            "<first-edition-repo-folder> --ship <ship-id> " +
            "[--output <folder>]");
    }
}
