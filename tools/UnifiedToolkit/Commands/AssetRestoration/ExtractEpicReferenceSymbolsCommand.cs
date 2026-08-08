using UnifiedToolkit.AssetRestoration.Epic;

namespace UnifiedToolkit.Commands.AssetRestoration;

public static class ExtractEpicReferenceSymbolsCommand
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
                ?? throw new ArgumentException("--ship is required.");
            var referenceFolder = ReadOption(
                    args,
                    "--reference-folder")
                ?? throw new ArgumentException(
                    "--reference-folder is required.");
            var symbolId = ReadOption(args, "--symbol")
                ?? "fore-turret-arrow";

            var result = EpicReferenceSymbolExtractor.Extract(
                repositoryRoot,
                shipId,
                referenceFolder,
                symbolId);

            Console.WriteLine(
                "UnifiedToolkit Phase 15C-R9 Epic Reference Symbol Extraction");
            Console.WriteLine(
                "============================================================");
            Console.WriteLine(
                $"Repository:             {repositoryRoot}");
            Console.WriteLine(
                $"Ship:                   {result.ShipId}");
            Console.WriteLine(
                $"Symbol:                 {result.SymbolId}");
            Console.WriteLine(
                $"Reference image:        {result.SourceImagePath}");
            Console.WriteLine(
                $"Source dimensions:      {result.SourceWidth} x {result.SourceHeight}");
            Console.WriteLine(
                $"Mask dimensions:        {result.MaskWidth} x {result.MaskHeight}");
            Console.WriteLine(
                $"Retained red pixels:    {result.RetainedPixels}");
            Console.WriteLine(
                $"Mask:                   {result.MaskPath}");
            Console.WriteLine(
                $"Metadata:               {result.MetadataPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Epic reference-symbol extraction failed: {exception.Message}");
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

    private static void ShowUsage() =>
        Console.WriteLine(
            "Usage: UnifiedToolkit extract-epic-reference-symbols " +
            "<first-edition-repo-folder> --ship <ship-id> " +
            "--reference-folder <folder> " +
            "[--symbol fore-turret-arrow]");
}
