using UnifiedToolkit.Assets;
using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Reports;

namespace UnifiedToolkit.Commands;

public static class BuildAssetCatalogueCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit build-asset-catalogue <repo-folder> <legacy-save.json> [mapping-folder] [--allow-source-errors] [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryFolder = Path.GetFullPath(args[0]);
            var legacySave = Path.GetFullPath(args[1]);
            var allowSourceErrors = args.Any(x => x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));
            var mappingFolder = ResolveMappingFolder(args.Skip(2).ToArray());
            var outputFolder = ResolveOutputFolder(args, Path.Combine(repositoryFolder, "_unifiedtoolkit_reports", "assets"));

            Console.WriteLine("UnifiedToolkit Structural Asset Catalogue");
            Console.WriteLine("===============================================");
            Console.WriteLine();
            Console.WriteLine($"Repository folder: {repositoryFolder}");
            Console.WriteLine($"Legacy save:       {legacySave}");
            Console.WriteLine($"Mapping folder:    {mappingFolder}");
            Console.WriteLine();

            var catalogue = AssetCatalogueBuilder.Build(repositoryFolder, legacySave);
            var build = FirstEditionRepositoryBuilder.Build(repositoryFolder, mappingFolder, allowSourceErrors);
            var requirements = FirstEditionAssetMatcher.Requirements(build.Repository);
            var matches = FirstEditionAssetMatcher.Match(build.Repository, catalogue);
            var reviews = FirstEditionAssetMatcher.BuildReview(requirements, matches);

            Directory.CreateDirectory(outputFolder);
            var cataloguePath = Path.Combine(outputFolder, "asset-catalogue.json");
            var matchesPath = Path.Combine(outputFolder, "first-edition-asset-candidates.csv");
            var roleCoveragePath = Path.Combine(outputFolder, "asset-role-coverage.csv");
            var reviewPath = Path.Combine(outputFolder, "asset-resolutions.review.json");
            var summaryPath = Path.Combine(outputFolder, "asset-resolution-summary.csv");

            AssetCatalogueReports.WriteCatalogue(catalogue, cataloguePath);
            AssetCatalogueReports.WriteMatches(matches, matchesPath);
            AssetCatalogueReports.WriteRoleCoverage(requirements, matches, roleCoveragePath);
            AssetCatalogueReports.WriteResolutionReview(reviews, reviewPath);
            AssetCatalogueReports.WriteResolutionSummary(reviews, summaryPath);

            var repositoryAssets = catalogue.Assets.Count(x => x.SourceKind == AssetSourceKind.RepositoryFile);
            var templates = catalogue.Assets.Count(x => x.SourceKind == AssetSourceKind.LegacySaveObject);
            var urls = catalogue.Assets.Count(x => x.SourceKind == AssetSourceKind.LegacySaveUrl);
            var matchedEntities = reviews.Where(x => x.Candidates.Count > 0).Select(x => x.Entity.SemanticKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var totalEntities = reviews.Select(x => x.Entity.SemanticKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var coveredRoles = reviews.Count(x => x.Candidates.Count > 0);
            var missingRoles = reviews.Count(x => x.Required && x.Candidates.Count == 0);
            var optionalRoles = reviews.Count(x => !x.Required && x.Candidates.Count == 0);
            var autoApprovable = reviews.Count(x => x.Candidates.FirstOrDefault()?.ConfidenceBand == AssetConfidenceBand.AutoApprovable);
            var reviewRequired = reviews.Count(x => x.Candidates.FirstOrDefault()?.ConfidenceBand == AssetConfidenceBand.ReviewRequired);

            Console.WriteLine($"Mapping version:     {build.MappingVersion}");
            Console.WriteLine($"Repository assets:   {repositoryAssets}");
            Console.WriteLine($"TTS templates:       {templates}");
            Console.WriteLine($"Remote asset URLs:   {urls}");
            Console.WriteLine($"Total asset records: {catalogue.Assets.Count}");
            Console.WriteLine($"Match candidates:    {matches.Count}");
            Console.WriteLine($"Semantic entities:   {matchedEntities} / {totalEntities} with at least one role candidate");
            Console.WriteLine($"Asset roles covered: {coveredRoles} / {reviews.Count}");
            Console.WriteLine($"Auto-approvable roles: {autoApprovable}");
            Console.WriteLine($"Review-required roles: {reviewRequired}");
            Console.WriteLine($"Required roles missing: {missingRoles}");
            Console.WriteLine($"Optional roles absent:  {optionalRoles}");
            Console.WriteLine($"Catalogue:           {cataloguePath}");
            Console.WriteLine($"Candidates report:   {matchesPath}");
            Console.WriteLine($"Role coverage:       {roleCoveragePath}");
            Console.WriteLine($"Editable review:     {reviewPath}");
            Console.WriteLine($"Review summary:      {summaryPath}");
            Console.WriteLine();
            Console.WriteLine("Only structurally compatible recommendations are included. Review artifacts do not create live mappings.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Asset catalogue failed: {ex.Message}");
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

    private static string ResolveOutputFolder(string[] args, string defaultPath)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(args[i + 1]);
        return defaultPath;
    }
}
