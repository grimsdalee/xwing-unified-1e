using UnifiedToolkit.Assets;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Reports;

namespace UnifiedToolkit.Commands;

public static class ApproveAssetResolutionsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit approve-asset-resolutions <asset-resolutions.review.json> <asset-catalogue.json> [mapping-folder] [--version <asset-version>] [--apply]");
            return 1;
        }

        try
        {
            var reviewPath = Path.GetFullPath(args[0]);
            var cataloguePath = Path.GetFullPath(args[1]);
            var mappingFolder = ResolveMappingFolder(args.Skip(2).ToArray());
            var assetVersion = ResolveVersion(args) ?? "0.1.0";
            var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));

            var reviews = AssetResolutionApproval.LoadReview(reviewPath);
            var catalogue = AssetResolutionApproval.LoadCatalogue(cataloguePath);
            var result = AssetResolutionApproval.Build(reviews, catalogue);
            var semanticVersion = LoadSemanticMappingVersion(mappingFolder);

            var approvedAssignments = result.Mappings.Count + result.SharedAssets.Sum(x => x.SemanticKeys.Count);

            Console.WriteLine("UnifiedToolkit Asset Resolution Approval");
            Console.WriteLine("=========================================");
            Console.WriteLine();
            Console.WriteLine($"Semantic mapping version: {semanticVersion}");
            Console.WriteLine($"Asset mapping version:    {assetVersion}");
            Console.WriteLine($"Review entries:           {reviews.Count}");
            Console.WriteLine($"Approved automatically:   {approvedAssignments}");
            Console.WriteLine($"Pending review:           {result.PendingReview}");
            Console.WriteLine($"Optional not required:    {result.OptionalNotRequired}");
            Console.WriteLine($"Missing required:         {result.MissingRequired}");
            Console.WriteLine($"Validation issues:        {result.ValidationIssues.Count}");

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
                Console.WriteLine("Preview only. Re-run with --apply to write live asset mappings and dispositions.");
                return 0;
            }

            var assetsFolder = Path.Combine(mappingFolder, "assets");
            var backupFolder = CreateBackup(assetsFolder);
            Directory.CreateDirectory(assetsFolder);

            var manifest = new AssetMappingSetManifest
            {
                AssetMappingVersion = assetVersion,
                SemanticMappingVersion = semanticVersion,
                ApprovedMappings = approvedAssignments,
                SharedAssignments = result.SharedAssets.Sum(x => x.SemanticKeys.Count),
                PendingReview = result.PendingReview,
                OptionalNotRequired = result.OptionalNotRequired,
                MissingRequired = result.MissingRequired
            };

            AssetMappingReports.WriteJson(result.Mappings, Path.Combine(assetsFolder, "asset-mappings.json"));
            AssetMappingReports.WriteJson(result.SharedAssets, Path.Combine(assetsFolder, "shared-assets.json"));
            AssetMappingReports.WriteJson(result.Dispositions, Path.Combine(assetsFolder, "asset-dispositions.json"));
            AssetMappingReports.WriteJson(manifest, Path.Combine(assetsFolder, "asset-mapping-set.json"));
            AssetMappingReports.WriteValidation(result.ValidationIssues, Path.Combine(assetsFolder, "asset-mapping-validation.csv"));
            AssetMappingReports.WriteSummary(result, Path.Combine(assetsFolder, "asset-mapping-summary.csv"));

            Console.WriteLine($"Applied:                   True");
            Console.WriteLine($"Asset mapping folder:      {assetsFolder}");
            Console.WriteLine($"Backup folder:             {(string.IsNullOrEmpty(backupFolder) ? "(none - first asset approval)" : backupFolder)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Asset approval failed: {ex.Message}");
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

    private static string LoadSemanticMappingVersion(string mappingFolder)
    {
        var path = Path.Combine(mappingFolder, "mapping-set.json");
        if (!File.Exists(path)) return "unknown";
        var mappingSet = ConversionMappingLoader.Load(mappingFolder);
        return mappingSet.Version;
    }

    private static string CreateBackup(string assetsFolder)
    {
        if (!Directory.Exists(assetsFolder) || !Directory.EnumerateFiles(assetsFolder).Any()) return "";

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
