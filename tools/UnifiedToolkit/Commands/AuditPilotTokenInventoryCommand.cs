using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class AuditPilotTokenInventoryCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            string? output = null;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) output = args[++i];
                else { Console.WriteLine($"Unknown or incomplete option: {args[i]}"); ShowUsage(); return 1; }
            }
            Console.WriteLine("UnifiedToolkit Pilot Token Inventory Audit");
            Console.WriteLine("==========================================");
            Console.WriteLine();
            var result = new PilotTokenInventoryAuditService().Audit(args[0], output);
            Console.WriteLine($"Pilot records:                  {result.TotalPilotRecords}");
            Console.WriteLine($"Epic pilot records excluded:    {result.EpicPilotRecords}");
            Console.WriteLine($"Non-Epic pilot target:          {result.NonEpicPilotRecords}");
            Console.WriteLine($"Generated tokens matched:       {result.GeneratedTokensMatched}");
            Console.WriteLine($"Missing pilot tokens:           {result.MissingTokens}");
            Console.WriteLine($"Pilot-card PNG files discovered:{result.PilotCardFiles,5}");
            Console.WriteLine($"Generated PNG files discovered: {result.GeneratedTokenFiles}");
            Console.WriteLine($"Unmatched pilot-card files:      {result.UnmatchedPilotCardFiles}");
            Console.WriteLine($"Unmatched generated files:       {result.UnmatchedGeneratedTokenFiles}");
            Console.WriteLine($"Nien Nunb search hits:           {result.NienNunbSearchHits}");
            Console.WriteLine();
            Console.WriteLine($"Inventory: {result.InventoryCsv}");
            Console.WriteLine($"Nien Nunb search: {result.NienNunbSearchCsv}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
    }
    private static void ShowUsage() => Console.WriteLine("  audit-pilot-token-inventory <first-edition-repo-folder> [--output <folder>]");
}
