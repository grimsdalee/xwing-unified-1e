using UnifiedToolkit.Assets;

namespace UnifiedToolkit.Commands;

public static class ReviewShipAssetsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit review-ship-assets <asset-resolutions.review.json> [mapping-folder] [--output <review.json>]");
            return 1;
        }

        try
        {
            var fullReviewPath = Path.GetFullPath(args[0]);
            var mappingFolder = ResolveMappingFolder(args.Skip(1).ToArray());
            var outputPath = ResolveOutput(args) ?? Path.Combine(
                Path.GetDirectoryName(fullReviewPath)!,
                "ship-assets.review.json");

            var result = ShipAssetReviewService.Build(fullReviewPath, mappingFolder);
            ShipAssetReviewService.WriteDocument(result.Document, outputPath);

            Console.WriteLine("UnifiedToolkit Assisted Ship Asset Review");
            Console.WriteLine("==========================================");
            Console.WriteLine();
            Console.WriteLine($"Current asset version: {result.Document.SourceAssetMappingVersion}");
            Console.WriteLine($"Already approved:      {result.AlreadyApproved}");
            Console.WriteLine($"Roles for review:      {result.PendingRoles}");
            Console.WriteLine($"Clear recommendations: {result.ClearRecommendations}");
            Console.WriteLine($"Editable review:       {outputPath}");
            Console.WriteLine();
            Console.WriteLine("Set Decision to Approve, copy a candidate AssetId into SelectedAssetId, and provide a reason.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ship asset review failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveMappingFolder(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase)) i++;
                continue;
            }
            return Path.GetFullPath(args[i]);
        }

        return Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
    }

    private static string? ResolveOutput(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        return null;
    }
}
