using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Commands;

public static class ApproveFirstEditionUpgradesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: UnifiedToolkit approve-first-edition-upgrades <upgrades.canonical.proposed.json> <upgrade-source-alternates.proposed.json> <official-first-edition-upgrade-matches.csv> [mapping-folder] --version <version> [--apply]");
            return 1;
        }

        var versionIndex = Array.FindIndex(args, x => x.Equals("--version", StringComparison.OrdinalIgnoreCase));
        if (versionIndex < 0 || versionIndex + 1 >= args.Length) { Console.Error.WriteLine("--version is required."); return 1; }

        var canonical = Path.GetFullPath(args[0]);
        var alternates = Path.GetFullPath(args[1]);
        var candidates = Path.GetFullPath(args[2]);
        var mappingArg = args.Skip(3).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal) && x != args[versionIndex + 1]);
        var mappingFolder = mappingArg is null ? Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition") : Path.GetFullPath(mappingArg);
        var targetVersion = args[versionIndex + 1];
        var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));

        try
        {
            var current = ConversionMappingLoader.Load(mappingFolder).Version;
            var result = UpgradeMappingApprovalService.Execute(canonical, alternates, candidates, mappingFolder, targetVersion, apply);
            Console.WriteLine("UnifiedToolkit First Edition Upgrade Approval");
            Console.WriteLine("=============================================");
            Console.WriteLine();
            Console.WriteLine($"Current version:     {current}");
            Console.WriteLine($"Target version:      {targetVersion}");
            Console.WriteLine($"Canonical upgrades:  {result.CanonicalCount}");
            Console.WriteLine($"Alternate sources:   {result.AlternateCount}");
            Console.WriteLine($"Dispositions:        {result.DispositionCount}");
            Console.WriteLine($"Validation issues:   {result.ValidationIssues.Count}");
            foreach (var issue in result.ValidationIssues) Console.WriteLine($"  {issue}");
            if (!apply) Console.WriteLine("\nPreview only. Re-run with --apply to write live upgrade mappings and dispositions.");
            else if (result.Applied) { Console.WriteLine($"Applied:             True"); Console.WriteLine($"Backup folder:       {result.BackupFolder}"); }
            return result.ValidationIssues.Count > 0 ? 2 : 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Upgrade approval failed: {ex.Message}"); return 1; }
    }
}
