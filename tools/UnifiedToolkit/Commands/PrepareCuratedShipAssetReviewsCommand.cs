using UnifiedToolkit.Assets;

namespace UnifiedToolkit.Commands;

public static class PrepareCuratedShipAssetReviewsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit prepare-curated-ship-asset-reviews <ship-assets.review.json> [--output <folder>]");
            return 1;
        }

        try
        {
            var reviewPath = Path.GetFullPath(args[0]);
            var outputFolder = ResolveOutput(args)
                ?? Path.GetDirectoryName(reviewPath)
                ?? throw new InvalidOperationException("Could not determine the output folder.");

            Directory.CreateDirectory(outputFolder);
            var result = CuratedShipAssetReviewService.Build(reviewPath);

            foreach (var pair in result.Documents)
            {
                var outputPath = Path.Combine(outputFolder, CuratedShipAssetReviewService.FileNameFor(pair.Key));
                ShipAssetReviewService.WriteDocument(pair.Value, outputPath);
            }

            Console.WriteLine("UnifiedToolkit Curated Ship Asset Reviews");
            Console.WriteLine("==========================================");
            Console.WriteLine();
            Console.WriteLine($"Current asset version: {result.SourceAssetMappingVersion}");
            Console.WriteLine($"Model roles:           {Count(result, AssetRole.ShipModel)}");
            Console.WriteLine($"Texture roles:         {Count(result, AssetRole.ShipTexture)}");
            Console.WriteLine($"Base roles:            {Count(result, AssetRole.ShipBase)}");
            Console.WriteLine($"Dial roles:            {Count(result, AssetRole.ShipDial)}");
            Console.WriteLine($"Total roles:           {result.TotalEntries}");
            Console.WriteLine($"Output folder:         {outputFolder}");
            Console.WriteLine();
            Console.WriteLine("No selections were pre-approved. Review and apply each role file independently.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Curated ship asset review generation failed: {ex.Message}");
            return 1;
        }
    }

    private static int Count(CuratedShipAssetReviewResult result, AssetRole role) =>
        result.Documents.TryGetValue(role, out var document) ? document.Entries.Count : 0;

    private static string? ResolveOutput(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        return null;
    }
}
