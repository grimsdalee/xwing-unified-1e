using System.Text.Json;
using UnifiedToolkit.Assets;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Commands;

public static class ApproveClearShipRecommendationsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit approve-clear-ship-recommendations <ship-assets.review.json> <asset-catalogue.json> [mapping-folder] --version <asset-version> [--output <reviewed.json>] [--apply]");
            return 1;
        }

        try
        {
            var reviewPath = Path.GetFullPath(args[0]);
            var cataloguePath = Path.GetFullPath(args[1]);
            var mappingFolder = ResolveMappingFolder(args.Skip(2).ToArray());
            var targetVersion = ResolveOption(args, "--version")
                ?? throw new ArgumentException("--version is required.");
            var outputPath = ResolveOption(args, "--output") is { } configuredOutput
                ? Path.GetFullPath(configuredOutput)
                : Path.Combine(Path.GetDirectoryName(reviewPath)!, "ship-assets.clear-recommendations.json");
            var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            var assetsFolder = Path.Combine(mappingFolder, "assets");

            var sourceDocument = ShipAssetReviewService.LoadDocument(reviewPath);
            var prepared = ClearShipAssetRecommendationService.Build(sourceDocument);
            ShipAssetReviewService.WriteDocument(prepared.Document, outputPath);

            var catalogue = AssetResolutionApproval.LoadCatalogue(cataloguePath);
            var approval = ReviewedShipAssetApprovalService.Build(prepared.Document, catalogue, assetsFolder);
            var semanticVersion = File.Exists(Path.Combine(mappingFolder, "mapping-set.json"))
                ? ConversionMappingLoader.Load(mappingFolder).Version
                : "unknown";
            var currentVersion = LoadCurrentAssetVersion(assetsFolder);

            Console.WriteLine("UnifiedToolkit Clear Ship Recommendation Approval");
            Console.WriteLine("=================================================");
            Console.WriteLine();
            Console.WriteLine($"Current asset version:    {currentVersion}");
            Console.WriteLine($"Target asset version:     {targetVersion}");
            Console.WriteLine($"Clear recommendations:    {prepared.ApprovedRecommendations}");
            Console.WriteLine($"New mappings prepared:    {approval.NewMappings.Count}");
            Console.WriteLine($"Still pending ship roles: {prepared.RemainingUnreviewed}");
            Console.WriteLine($"Validation issues:        {approval.ValidationIssues.Count}");
            Console.WriteLine($"Reviewed output:          {outputPath}");

            if (approval.ValidationIssues.Count > 0)
            {
                Console.WriteLine();
                foreach (var issue in approval.ValidationIssues.Take(20))
                    Console.WriteLine($"  - {issue}");
                if (approval.ValidationIssues.Count > 20)
                    Console.WriteLine($"  ... {approval.ValidationIssues.Count - 20} more");
                Console.WriteLine();
                Console.WriteLine("Approval refused because validation issues were found.");
                return 1;
            }

            if (!apply)
            {
                Console.WriteLine();
                Console.WriteLine("Preview only. Re-run with --apply to merge the clear ship recommendations.");
                return 0;
            }

            var backupFolder = CreateBackup(assetsFolder);
            ReviewedShipAssetApprovalService.Apply(approval, assetsFolder, targetVersion, semanticVersion);

            Console.WriteLine($"Applied:                   True");
            Console.WriteLine($"Asset mapping folder:      {assetsFolder}");
            Console.WriteLine($"Backup folder:             {backupFolder}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Clear ship recommendation approval failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveMappingFolder(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (args[i].Equals("--version", StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                    i++;
                continue;
            }

            return Path.GetFullPath(args[i]);
        }

        return Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
    }

    private static string? ResolveOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static string LoadCurrentAssetVersion(string assetsFolder)
    {
        var path = Path.Combine(assetsFolder, "asset-mapping-set.json");
        if (!File.Exists(path)) return "none";
        var manifest = JsonSerializer.Deserialize<AssetMappingSetManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return manifest?.AssetMappingVersion ?? "unknown";
    }

    private static string CreateBackup(string assetsFolder)
    {
        if (!Directory.Exists(assetsFolder)) return "(none)";

        var backupFolder = Path.Combine(
            Path.GetDirectoryName(assetsFolder)!,
            "backups",
            $"assets-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backupFolder);

        foreach (var file in Directory.EnumerateFiles(assetsFolder))
            File.Copy(file, Path.Combine(backupFolder, Path.GetFileName(file)), true);

        return backupFolder;
    }
}
