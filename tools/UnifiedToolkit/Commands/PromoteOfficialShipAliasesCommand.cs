using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Commands;

public static class PromoteOfficialShipAliasesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        var proposalsPath = Path.GetFullPath(args[0]);
        var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        var versionIndex = Array.FindIndex(args, x => x.Equals("--version", StringComparison.OrdinalIgnoreCase));
        var version = versionIndex >= 0 && versionIndex + 1 < args.Length ? args[versionIndex + 1] : "0.5.0";
        var mappingFolder = FindMappingFolder(args, versionIndex);

        try
        {
            var result = apply
                ? OfficialAliasPromotionService.Apply(proposalsPath, mappingFolder, version)
                : OfficialAliasPromotionService.Preview(proposalsPath, mappingFolder, version);

            Console.WriteLine("UnifiedToolkit Official Ship Alias Promotion");
            Console.WriteLine("============================================");
            Console.WriteLine();
            Console.WriteLine($"Current version:        {result.CurrentVersion}");
            Console.WriteLine($"Target version:         {result.TargetVersion}");
            Console.WriteLine($"Proposed aliases:       {result.ProposedCount}");
            Console.WriteLine($"Remaining dispositions: {result.RemainingDispositions.Count}");
            Console.WriteLine($"Validation issues:      {result.ValidationIssues.Count}");
            foreach (var issue in result.ValidationIssues)
                Console.WriteLine($"  {issue.Severity}: {issue.Code} - {issue.SourceId} {issue.Message}");

            if (result.ValidationIssues.Any(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))) return 2;
            if (!apply)
            {
                Console.WriteLine();
                Console.WriteLine("Preview only. Re-run with --apply to promote mappings and remove their dispositions.");
                return 0;
            }

            Console.WriteLine($"Applied:                {result.Applied}");
            Console.WriteLine($"Backup folder:          {result.BackupFolder}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Official alias promotion failed: {ex.Message}");
            return 1;
        }
    }

    private static string FindMappingFolder(string[] args, int versionIndex)
    {
        for (var index = 1; index < args.Length; index++)
        {
            if (index == versionIndex || index == versionIndex + 1) continue;
            if (args[index].Equals("--apply", StringComparison.OrdinalIgnoreCase)) continue;
            if (args[index].StartsWith("--", StringComparison.Ordinal)) continue;
            return Path.GetFullPath(args[index]);
        }
        return Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
    }

    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit promote-official-ship-aliases <official-alias-mappings.proposed.json> [mapping-folder] [--version <version>] [--apply]");
}
