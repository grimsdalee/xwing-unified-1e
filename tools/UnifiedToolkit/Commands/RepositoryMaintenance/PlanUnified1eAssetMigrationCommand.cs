using System.Text;
using System.Text.Json;
using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class PlanUnified1eAssetMigrationCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: plan-unified1e-asset-migration <first-edition-repo-folder> [--output <folder>]");
            return 1;
        }
        try
        {
            var root = Path.GetFullPath(args[0]);
            var output = ResolveOutput(root, args);
            var plan = new Unified1eAssetMigrationPlanner().Build(root);
            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "unified1e-asset-migration-plan.json");
            var csvPath = Path.Combine(output, "unified1e-asset-migration-plan.csv");
            var mdPath = Path.Combine(output, "UNIFIED1E-ASSET-MIGRATION-PLAN.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), new UTF8Encoding(false));
            WriteCsv(csvPath, plan);
            WriteMarkdown(mdPath, plan);
            Console.WriteLine("UnifiedToolkit First Edition Asset Migration Planner");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Repository:             {root}");
            Console.WriteLine($"Ship folders:           {plan.ShipFolders}");
            Console.WriteLine($"Base folders:           {plan.BaseFolders}");
            Console.WriteLine($"Additional assets:      {plan.AdditionalFiles}");
            Console.WriteLine($"Ready:                  {plan.Ready}");
            Console.WriteLine($"Manual review required: {plan.ManualReviewRequired}");
            Console.WriteLine($"Conflicts:              {plan.Conflicts}");
            Console.WriteLine($"Plan:                   {jsonPath}");
            Console.WriteLine();
            Console.WriteLine("Planning only. No files were copied, moved, deleted, or rewired.");
            return plan.ManualReviewRequired == 0 && plan.Conflicts == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unified1e asset migration planning failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveOutput(string root, IReadOnlyList<string> args)
    {
        for (var i = 1; i < args.Count; i++)
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[++i]);
        return Path.Combine(root, "_unifiedtoolkit_reports", "phase13", "unified1e-asset-migration");
    }

    private static void WriteCsv(string path, Unified1eAssetMigrationPlan plan)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("Kind,SourcePath,DestinationPath,CanonicalFirstEditionId,CurrentFolderClass,FirstEditionBaseSize,FileCount,SizeBytes,Status,Reasons");
        foreach (var e in plan.Entries) w.WriteLine(string.Join(',', Q(e.Kind), Q(e.SourcePath), Q(e.DestinationPath), Q(e.CanonicalFirstEditionId), Q(e.CurrentFolderClass), Q(e.FirstEditionBaseSize), e.FileCount, e.SizeBytes, Q(e.Status), Q(string.Join(" | ", e.Reasons))));
    }

    private static void WriteMarkdown(string path, Unified1eAssetMigrationPlan plan)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("# Unified First Edition Asset Migration Plan\n");
        w.WriteLine("This is a copy-only planning report. No repository files were changed.\n");
        w.WriteLine($"- Ship folders: {plan.ShipFolders}");
        w.WriteLine($"- Base folders: {plan.BaseFolders}");
        w.WriteLine($"- Additional assets: {plan.AdditionalFiles}");
        w.WriteLine($"- Manual review required: {plan.ManualReviewRequired}");
        w.WriteLine($"- Conflicts: {plan.Conflicts}\n");
        w.WriteLine("| Kind | Source | Destination | Status | Reason |");
        w.WriteLine("|---|---|---|---|---|");
        foreach (var e in plan.Entries) w.WriteLine($"| {e.Kind} | `{e.SourcePath}` | `{e.DestinationPath}` | {e.Status} | {string.Join("; ", e.Reasons)} |");
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
