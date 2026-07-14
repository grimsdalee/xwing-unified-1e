using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Pilots;

namespace UnifiedToolkit.Commands;

public static class ApproveFirstEditionPilotsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { ShowUsage(); return 1; }
        var canonical = Path.GetFullPath(args[0]);
        var alternates = Path.GetFullPath(args[1]);
        var apply = args.Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        var versionIndex = Array.FindIndex(args, x => x.Equals("--version", StringComparison.OrdinalIgnoreCase));
        var targetVersion = versionIndex >= 0 && versionIndex + 1 < args.Length ? args[versionIndex + 1] : "";
        var positional = args.Where((x, i) => i > 1 && !x.StartsWith("--") && i != versionIndex + 1).ToList();
        var mappingFolder = positional.Count > 0 ? Path.GetFullPath(positional[0]) : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");

        if (string.IsNullOrWhiteSpace(targetVersion)) { Console.Error.WriteLine("A target version is required."); ShowUsage(); return 1; }
        try
        {
            var current = ConversionMappingLoader.Load(mappingFolder);
            var result = PilotMappingApprovalService.Execute(canonical, alternates, mappingFolder, targetVersion, apply);
            Console.WriteLine("UnifiedToolkit First Edition Pilot Approval");
            Console.WriteLine("===========================================");
            Console.WriteLine();
            Console.WriteLine($"Current version:    {current.Version}");
            Console.WriteLine($"Target version:     {targetVersion}");
            Console.WriteLine($"Canonical pilots:   {result.CanonicalCount}");
            Console.WriteLine($"Alternate sources:  {result.AlternateCount}");
            Console.WriteLine($"Validation issues:  {result.ValidationIssues.Count}");
            foreach (var issue in result.ValidationIssues) Console.WriteLine($"  - {issue}");
            if (result.ValidationIssues.Count > 0) return 1;
            if (!apply) Console.WriteLine("\nPreview only. Re-run with --apply to write live pilot mappings.");
            else { Console.WriteLine($"Applied:            {result.Applied}"); Console.WriteLine($"Backup folder:      {result.BackupFolder}"); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Pilot approval failed: {ex.Message}"); return 1; }
    }

    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit approve-first-edition-pilots <pilots.canonical.proposed.json> <pilot-source-alternates.proposed.json> [mapping-folder] --version <version> [--apply]");
}
