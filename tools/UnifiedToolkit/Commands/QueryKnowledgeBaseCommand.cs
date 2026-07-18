using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.Commands;

public static class QueryKnowledgeBaseCommand
{
    private const int DefaultLimit = 50;

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = args[0];
            var query = args[1].ToLowerInvariant();
            var remaining = args.Skip(2).ToArray();
            var service = new KnowledgeBaseQueryService();
            var ukb = service.Load(repositoryRoot);

            Console.WriteLine("UnifiedToolkit Knowledge Base Query");
            Console.WriteLine("===================================");
            Console.WriteLine();

            return query switch
            {
                "stats" => ShowStatistics(ukb),
                "asset" => ShowAsset(service, ukb, remaining),
                "search" => Search(service, ukb, remaining),
                "duplicates" => ShowDuplicates(ukb, remaining),
                "unavailable" => ShowUnavailable(service, ukb, remaining),
                "validation" => ShowValidation(ukb, remaining),
                _ => UnknownQuery(query)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static int ShowStatistics(UnifiedKnowledgeBase ukb)
    {
        Console.WriteLine($"Schema version:       {ukb.SchemaVersion}");
        Console.WriteLine($"Generated UTC:        {ukb.GeneratedUtc:O}");
        Console.WriteLine($"Repository:           {ukb.RepositoryRoot}");
        Console.WriteLine();
        Console.WriteLine($"Asset files:          {ukb.Statistics.FileCount:N0}");
        Console.WriteLine($"Unique assets:        {ukb.Statistics.UniqueAssetCount:N0}");
        Console.WriteLine($"Duplicate files:      {ukb.Statistics.DuplicateFileCount:N0}");
        Console.WriteLine($"Unavailable sources:  {ukb.Statistics.UnavailableSourceCount:N0}");
        Console.WriteLine($"Total size:           {FormatBytes(ukb.Statistics.TotalBytes)}");
        Console.WriteLine();

        Console.WriteLine("By warehouse:");
        foreach (var item in ukb.Statistics.ByWarehouse.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
        {
            Console.WriteLine($"  {item.Key,-20} {item.Value,8:N0}");
        }

        Console.WriteLine();
        Console.WriteLine("By asset type:");
        foreach (var item in ukb.Statistics.ByAssetType.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
        {
            Console.WriteLine($"  {item.Key,-20} {item.Value,8:N0}");
        }

        Console.WriteLine();
        Console.WriteLine($"Validation errors:    {ukb.Validation.Issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)):N0}");
        Console.WriteLine($"Validation warnings:  {ukb.Validation.Issues.Count(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)):N0}");
        return 0;
    }

    private static int ShowAsset(KnowledgeBaseQueryService service, UnifiedKnowledgeBase ukb, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Missing asset ID, SHA-256, repository path or exact filename.");
            return 1;
        }

        var target = string.Join(' ', args);
        var asset = service.FindAsset(ukb, target);
        if (asset is null)
        {
            var candidates = service.SearchAssets(ukb, target).Take(10).ToList();
            Console.WriteLine($"No exact asset found for: {target}");
            if (candidates.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Closest matches:");
                foreach (var candidate in candidates)
                {
                    PrintAssetSummary(candidate);
                }
            }
            return 2;
        }

        Console.WriteLine($"Asset ID:          {asset.AssetId}");
        Console.WriteLine($"SHA-256:           {asset.Sha256}");
        Console.WriteLine($"File:              {asset.FileName}");
        Console.WriteLine($"Repository path:   {asset.RepositoryPath}");
        Console.WriteLine($"Type:              {asset.AssetType}");
        Console.WriteLine($"Warehouse:         {asset.Warehouse}");
        Console.WriteLine($"Generated:         {asset.IsGenerated}");
        Console.WriteLine($"Size:              {FormatBytes(asset.SizeBytes)} ({asset.SizeBytes:N0} bytes)");
        Console.WriteLine($"Availability:      {asset.Availability}");
        Console.WriteLine($"Release required:  {FormatNullable(asset.ReleaseRequired)}");
        Console.WriteLine();
        Console.WriteLine($"Source references: {asset.SourceReferences.Count:N0}");
        foreach (var source in asset.SourceReferences)
        {
            Console.WriteLine($"  [{source.SourceSystem}] {source.SourceLocation}");
            Console.WriteLine($"    Import status: {source.ImportStatus}");
            if (source.JsonPaths.Count > 0)
            {
                Console.WriteLine($"    JSON paths:    {source.JsonPaths.Count:N0}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Referenced by:     {asset.ReferencedBy.Count:N0}");
        foreach (var reference in asset.ReferencedBy)
        {
            Console.WriteLine($"  {reference.EntityType}:{reference.EntityId} ({reference.Role})");
        }

        var duplicate = ukb.Domains.DuplicateGroups.FirstOrDefault(group =>
            group.Sha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine();
        Console.WriteLine($"Duplicate paths:   {(duplicate?.RepositoryPaths.Count ?? 1) - 1:N0}");
        if (duplicate is not null)
        {
            foreach (var path in duplicate.RepositoryPaths)
            {
                Console.WriteLine($"  {path}");
            }
        }

        return 0;
    }

    private static int Search(KnowledgeBaseQueryService service, UnifiedKnowledgeBase ukb, string[] args)
    {
        var parsed = ParseTermAndLimit(args);
        if (string.IsNullOrWhiteSpace(parsed.Term))
        {
            Console.WriteLine("Missing search text.");
            return 1;
        }

        var results = service.SearchAssets(ukb, parsed.Term);
        Console.WriteLine($"Search:  {parsed.Term}");
        Console.WriteLine($"Matches: {results.Count:N0}");
        Console.WriteLine($"Showing: {Math.Min(parsed.Limit, results.Count):N0}");
        Console.WriteLine();

        foreach (var asset in results.Take(parsed.Limit))
        {
            PrintAssetSummary(asset);
        }

        return 0;
    }

    private static int ShowDuplicates(UnifiedKnowledgeBase ukb, string[] args)
    {
        var limit = ParseLimit(args, DefaultLimit);
        var groups = ukb.Domains.DuplicateGroups
            .OrderByDescending(group => group.RepositoryPaths.Count)
            .ThenBy(group => group.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"Duplicate groups: {groups.Count:N0}");
        Console.WriteLine($"Duplicate files:  {groups.Sum(group => group.RepositoryPaths.Count - 1):N0}");
        Console.WriteLine($"Showing groups:    {Math.Min(limit, groups.Count):N0}");
        Console.WriteLine();

        foreach (var group in groups.Take(limit))
        {
            Console.WriteLine($"{group.AssetId}  copies={group.RepositoryPaths.Count:N0}");
            foreach (var path in group.RepositoryPaths)
            {
                Console.WriteLine($"  {path}");
            }
            Console.WriteLine();
        }

        return 0;
    }

    private static int ShowUnavailable(KnowledgeBaseQueryService service, UnifiedKnowledgeBase ukb, string[] args)
    {
        var parsed = ParseTermAndLimit(args, allowEmptyTerm: true);
        var sources = service.SearchUnavailable(ukb, parsed.Term);

        Console.WriteLine($"Unavailable sources: {sources.Count:N0}");
        Console.WriteLine($"Showing:             {Math.Min(parsed.Limit, sources.Count):N0}");
        if (!string.IsNullOrWhiteSpace(parsed.Term))
        {
            Console.WriteLine($"Filter:              {parsed.Term}");
        }
        Console.WriteLine();

        foreach (var source in sources.Take(parsed.Limit))
        {
            Console.WriteLine($"{source.SourceId}  [{source.Status}]  {source.AssetType}");
            Console.WriteLine($"  Host:       {source.Host}");
            Console.WriteLine($"  URL:        {source.SourceUrl}");
            Console.WriteLine($"  Reason:     {source.Reason}");
            Console.WriteLine($"  References: {source.JsonPaths.Count:N0}");
            Console.WriteLine();
        }

        return 0;
    }

    private static int ShowValidation(UnifiedKnowledgeBase ukb, string[] args)
    {
        var limit = ParseLimit(args, DefaultLimit);
        var issues = ukb.Validation.Issues
            .OrderBy(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"Valid:     {ukb.Validation.IsValid}");
        Console.WriteLine($"Errors:    {issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)):N0}");
        Console.WriteLine($"Warnings:  {issues.Count(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)):N0}");
        Console.WriteLine($"Showing:   {Math.Min(limit, issues.Count):N0}");
        Console.WriteLine();

        foreach (var issue in issues.Take(limit))
        {
            Console.WriteLine($"[{issue.Severity.ToUpperInvariant()}] {issue.Code} {issue.SubjectId}");
            Console.WriteLine($"  {issue.Message}");
        }

        return ukb.Validation.IsValid ? 0 : 2;
    }

    private static void PrintAssetSummary(KnowledgeBaseAsset asset)
    {
        Console.WriteLine($"{asset.AssetId}  [{asset.Warehouse}/{asset.AssetType}]  {FormatBytes(asset.SizeBytes)}");
        Console.WriteLine($"  {asset.RepositoryPath}");
    }

    private static (string Term, int Limit) ParseTermAndLimit(string[] args, bool allowEmptyTerm = false)
    {
        var limit = DefaultLimit;
        var terms = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--limit", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || !int.TryParse(args[++index], out limit) || limit < 1)
                {
                    throw new ArgumentException("--limit requires a positive integer.");
                }
                continue;
            }

            terms.Add(args[index]);
        }

        var term = string.Join(' ', terms).Trim();
        if (!allowEmptyTerm && term.Length == 0)
        {
            throw new ArgumentException("Search text is required.");
        }

        return (term, limit);
    }

    private static int ParseLimit(string[] args, int defaultLimit)
    {
        if (args.Length == 0)
        {
            return defaultLimit;
        }

        if (args.Length == 2
            && args[0].Equals("--limit", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[1], out var limit)
            && limit > 0)
        {
            return limit;
        }

        throw new ArgumentException("Expected optional argument: --limit <positive-number>");
    }

    private static string FormatNullable(bool? value) => value.HasValue ? value.Value.ToString() : "unknown";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static int UnknownQuery(string query)
    {
        Console.WriteLine($"Unknown query: {query}");
        ShowUsage();
        return 1;
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  query-knowledge-base <repo-folder> stats");
        Console.WriteLine("  query-knowledge-base <repo-folder> asset <asset-id|sha256|repository-path|filename>");
        Console.WriteLine("  query-knowledge-base <repo-folder> search <text> [--limit <number>]");
        Console.WriteLine("  query-knowledge-base <repo-folder> duplicates [--limit <number>]");
        Console.WriteLine("  query-knowledge-base <repo-folder> unavailable [text] [--limit <number>]");
        Console.WriteLine("  query-knowledge-base <repo-folder> validation [--limit <number>]");
    }
}
