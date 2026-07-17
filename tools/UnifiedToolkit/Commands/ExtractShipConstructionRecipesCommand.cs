using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class ExtractShipConstructionRecipesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit extract-ship-construction-recipes <unified-2.5-save.json> [--runtime-report <spawner-runtime-report.json>] [--output <folder>]");
            return 1;
        }

        try
        {
            var savePath = Path.GetFullPath(args[0]);
            var output = GetOption(args, "--output")
                ?? Path.Combine(Path.GetDirectoryName(savePath) ?? ".", "_ship-construction-recipes");
            output = Path.GetFullPath(output);

            var runtimeReport = GetOption(args, "--runtime-report");
            if (!string.IsNullOrWhiteSpace(runtimeReport))
                runtimeReport = Path.GetFullPath(runtimeReport);

            Console.WriteLine("UnifiedToolkit Phase 5D Revision 1 - Ship Construction Recipe Extraction");
            Console.WriteLine("========================================================================");
            Console.WriteLine();
            Console.WriteLine($"Unified 2.5 save: {savePath}");
            Console.WriteLine($"Runtime report:   {runtimeReport ?? "not supplied"}");
            Console.WriteLine($"Output folder:    {output}");
            Console.WriteLine();

            var report = ShipConstructionRecipeExtractor.Extract(savePath, output, runtimeReport);
            Directory.CreateDirectory(output);

            WriteJson(Path.Combine(output, "ship-construction-recipes.json"), report);
            WriteFunctionCsv(Path.Combine(output, "ship-construction-functions.csv"), report.Functions);
            WriteDependencyCsv(Path.Combine(output, "ship-construction-dependencies.csv"), report.Dependencies);
            WriteMarkdown(Path.Combine(output, "SHIP-CONSTRUCTION-RECIPE-REPORT.md"), report);

            Console.WriteLine($"Requested functions:        {report.Functions.Count}");
            Console.WriteLine($"Functions found:            {report.Functions.Count(x => x.Found)}");
            Console.WriteLine($"Distinct call references:   {report.Functions.Sum(x => x.Calls.Count)}");
            Console.WriteLine($"GUID-backed dependencies:   {report.Dependencies.Count}");
            Console.WriteLine($"Resolved dependencies:      {report.Dependencies.Count(x => x.Resolved)}");
            Console.WriteLine();
            Console.WriteLine("This command performs analysis only. It does not generate or modify TTS ship objects.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ship construction recipe extraction failed: {ex.Message}");
            return 1;
        }
    }

    private static string? GetOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private static void WriteFunctionCsv(string path, IEnumerable<ShipConstructionFunctionRecipe> functions)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Name,Found,CharacterOffset,CharacterLength,SourceFile,Calls,GuidSymbols,UrlLiterals");
        foreach (var function in functions)
        {
            var row = new[]
            {
                function.Name,
                function.Found.ToString(),
                function.CharacterOffset.ToString(),
                function.CharacterLength.ToString(),
                function.SourceFile,
                string.Join(" | ", function.Calls),
                string.Join(" | ", function.GuidSymbols),
                string.Join(" | ", function.UrlLiterals)
            };
            writer.WriteLine(string.Join(',', row.Select(Csv)));
        }
    }

    private static void WriteDependencyCsv(string path, IEnumerable<ShipConstructionDependency> dependencies)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Symbol,Guid,Resolved,ObjectName,ObjectNickname,ObjectPath,UsedByFunctions,Assets");
        foreach (var dependency in dependencies)
        {
            var assets = string.Join(" | ", dependency.Assets.Select(x => $"{x.Container}/{x.Role}: {x.Url}"));
            var row = new[]
            {
                dependency.Symbol,
                dependency.Guid,
                dependency.Resolved.ToString(),
                dependency.ObjectName,
                dependency.ObjectNickname,
                dependency.ObjectPath,
                string.Join(" | ", dependency.UsedByFunctions),
                assets
            };
            writer.WriteLine(string.Join(',', row.Select(Csv)));
        }
    }

    private static void WriteMarkdown(string path, ShipConstructionRecipeReport report)
    {
        var lines = new List<string>
        {
            "# Ship Construction Recipe Report",
            "",
            "## Scope",
            "",
            "This report extracts the Unified 2.5 Lua functions that construct a ship bundle. It does not generate any TTS objects.",
            "",
            "## Function coverage",
            ""
        };

        foreach (var function in report.Functions)
        {
            lines.Add($"### `{function.Name}`");
            lines.Add("");
            lines.Add($"- Found: {function.Found}");
            if (function.Found)
            {
                lines.Add($"- Source: `{function.SourceFile}`");
                lines.Add($"- Calls: {(function.Calls.Count == 0 ? "none" : string.Join(", ", function.Calls.Select(x => $"`{x}`"))) }");
                lines.Add($"- GUID symbols: {(function.GuidSymbols.Count == 0 ? "none" : string.Join(", ", function.GuidSymbols.Select(x => $"`{x}`"))) }");
                lines.Add($"- Asset-like string literals: {(function.UrlLiterals.Count == 0 ? "none" : string.Join(", ", function.UrlLiterals.Select(x => $"`{x}`"))) }");
            }
            lines.Add("");
        }

        lines.Add("## GUID-backed dependencies");
        lines.Add("");
        if (report.Dependencies.Count == 0)
        {
            lines.Add("No direct GUID-backed dependencies were identified in the requested function bodies.");
        }
        else
        {
            foreach (var dependency in report.Dependencies)
            {
                lines.Add($"- `{dependency.Symbol}` → `{dependency.Guid}`; resolved: {dependency.Resolved}; object: `{dependency.ObjectNickname}`");
            }
        }

        lines.Add("");
        lines.Add("## Findings");
        lines.Add("");
        lines.AddRange(report.Findings.Select(x => "- " + x));
        lines.Add("");
        lines.Add("## Next milestone");
        lines.Add("");
        lines.Add("Use the extracted source and dependency reports to define one evidence-based First Edition X-Wing construction recipe before implementing any runtime spawning changes.");
        lines.Add("");

        File.WriteAllLines(path, lines);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
