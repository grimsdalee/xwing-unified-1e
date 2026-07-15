using System.Text.Json;
using UnifiedToolkit.Assets;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Commands;

public static class ApplyReviewedShipAssetsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit apply-reviewed-ship-assets <ship-assets.review.json> <asset-catalogue.json> [mapping-folder] --version <asset-version> [--apply]");
            return 1;
        }

        try
        {
            var reviewPath = Path.GetFullPath(args[0]);
            var cataloguePath = Path.GetFullPath(args[1]);
            var mappingFolder = ResolveMappingFolder(args.Skip(2).ToArray());
            var targetVersion = ResolveVersion(args) ?? throw new ArgumentException("--version is required.");
            var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            var assetsFolder = Path.Combine(mappingFolder, "assets");

            var document = ShipAssetReviewService.LoadDocument(reviewPath);
            var catalogue = AssetResolutionApproval.LoadCatalogue(cataloguePath);
            var result = ReviewedShipAssetApprovalService.Build(document, catalogue, assetsFolder);
            var semanticVersion = File.Exists(Path.Combine(mappingFolder, "mapping-set.json"))
                ? ConversionMappingLoader.Load(mappingFolder).Version
                : "unknown";
            var currentVersion = LoadCurrentAssetVersion(assetsFolder);

            Console.WriteLine("UnifiedToolkit Reviewed Ship Asset Approval");
            Console.WriteLine("===========================================");
            Console.WriteLine();
            Console.WriteLine($"Current asset version:  {currentVersion}");
            Console.WriteLine($"Target asset version:   {targetVersion}");
            Console.WriteLine($"Reviewed selections:    {result.NewMappings.Count}");
            Console.WriteLine($"Still unreviewed:        {result.Unreviewed}");
            Console.WriteLine($"Validation issues:       {result.ValidationIssues.Count}");

            if (result.ValidationIssues.Count > 0)
            {
                Console.WriteLine();
                foreach (var issue in result.ValidationIssues.Take(20)) Console.WriteLine($"  - {issue}");
                if (result.ValidationIssues.Count > 20) Console.WriteLine($"  ... {result.ValidationIssues.Count - 20} more");
                Console.WriteLine();
                Console.WriteLine("Approval refused because validation issues were found.");
                return 1;
            }

            if (!apply)
            {
                Console.WriteLine();
                Console.WriteLine("Preview only. Re-run with --apply to merge reviewed ship asset mappings.");
                return 0;
            }

            var backupFolder = CreateBackup(assetsFolder);
            ReviewedShipAssetApprovalService.Apply(result, assetsFolder, targetVersion, semanticVersion);

            Console.WriteLine($"Applied:                 True");
            Console.WriteLine($"Asset mapping folder:    {assetsFolder}");
            Console.WriteLine($"Backup folder:           {backupFolder}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Reviewed ship asset approval failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveMappingFolder(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (args[i].Equals("--version", StringComparison.OrdinalIgnoreCase)) i++;
                continue;
            }
            return Path.GetFullPath(args[i]);
        }
        return Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
    }

    private static string? ResolveVersion(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--version", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static string LoadCurrentAssetVersion(string assetsFolder)
    {
        var path = Path.Combine(assetsFolder, "asset-mapping-set.json");
        if (!File.Exists(path)) return "none";
        var manifest = JsonSerializer.Deserialize<AssetMappingSetManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
