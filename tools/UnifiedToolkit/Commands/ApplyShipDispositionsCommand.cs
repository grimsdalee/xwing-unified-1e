using UnifiedToolkit.Conversion.Mapping.Dispositions;

namespace UnifiedToolkit.Commands;

public static class ApplyShipDispositionsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        var reviewPath = Path.GetFullPath(args[0]);
        var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        var versionIndex = Array.FindIndex(args, x => x.Equals("--version", StringComparison.OrdinalIgnoreCase));
        var version = versionIndex >= 0 && versionIndex + 1 < args.Length ? args[versionIndex + 1] : "0.4.1";
        var positional = args.Skip(1).Where(x => !x.StartsWith("--") && x != version).ToArray();
        var mappingFolder = positional.Length > 0 ? Path.GetFullPath(positional[0]) : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
        try
        {
            var result = apply ? ShipDispositionApprovalService.Apply(reviewPath, mappingFolder, version) : ShipDispositionApprovalService.Preview(reviewPath, mappingFolder, version);
            Console.WriteLine("UnifiedToolkit Ship Disposition Approval");
            Console.WriteLine("=========================================");
            Console.WriteLine();
            Console.WriteLine($"Current version:   {result.CurrentVersion}");
            Console.WriteLine($"Target version:    {result.TargetVersion}");
            Console.WriteLine($"Reviewed entries:  {result.ReviewedCount}");
            Console.WriteLine($"Validation issues: {result.ValidationIssues.Count}");
            foreach (var issue in result.ValidationIssues) Console.WriteLine($"  {issue.Severity}: {issue.Code} - {issue.SourceId} {issue.Message}");
            if (result.ValidationIssues.Any(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))) return 2;
            if (!apply) { Console.WriteLine(); Console.WriteLine("Preview only. Re-run with --apply to write the reviewed dispositions."); return 0; }
            Console.WriteLine($"Applied:           {result.Applied}");
            Console.WriteLine($"Backup folder:     {result.BackupFolder}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Disposition approval failed: {ex.Message}"); return 1; }
    }

    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit apply-ship-dispositions <ship-dispositions.review.json> [mapping-folder] [--version <version>] [--apply]");
}
