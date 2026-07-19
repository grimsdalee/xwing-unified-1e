using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class PreparePilotTokenExtractionReviewCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: prepare-pilot-token-extraction-review <first-edition-repo-folder> <existing-plan.json> [--output <folder>]");
            return 1;
        }
        var output = GetOption(args, "--output");
        Console.WriteLine("UnifiedToolkit Prepare Pilot Token Extraction Review v2");
        Console.WriteLine("======================================================");
        var result = new PilotTokenExtractionReviewService().Prepare(args[0], args[1], output);
        Console.WriteLine();
        Console.WriteLine($"Sheets:             {result.Sheets}");
        Console.WriteLine($"Pilot entries:      {result.Entries}");
        Console.WriteLine($"Sheets incomplete:  {result.UnresolvedSheets}");
        Console.WriteLine($"Mappings unresolved: {result.UnassignedMappings}");
        Console.WriteLine();
        Console.WriteLine($"Plan: {result.PlanFile}");
        Console.WriteLine($"HTML: {result.HtmlFile}");
        Console.WriteLine($"Mapping validation: {Path.Combine(result.OutputFolder, "pilot-token-mapping-validation.csv")}");
        return 0;
    }
    private static string? GetOption(string[] args, string name)
    {
        for (var i=0;i<args.Length-1;i++) if (args[i].Equals(name,StringComparison.OrdinalIgnoreCase)) return args[i+1];
        return null;
    }
}
