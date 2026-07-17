using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class InspectSpawnerRuntimeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine(
                "Usage: UnifiedToolkit inspect-spawner-runtime " +
                "<unified-2.5-save.json> [--output <folder>]");
            return 1;
        }

        try
        {
            var savePath = Path.GetFullPath(args[0]);
            var outputFolder = ResolveOutputFolder(
                args,
                Path.Combine(
                    Path.GetDirectoryName(savePath) ?? ".",
                    "_runtime-inspection"));

            Console.WriteLine(
                "UnifiedToolkit Phase 5C Revision 3 - Unified Runtime Inspection");
            Console.WriteLine(
                "==============================================================");
            Console.WriteLine();
            Console.WriteLine($"Unified 2.5 save: {savePath}");
            Console.WriteLine($"Output folder:    {outputFolder}");
            Console.WriteLine();

            var report = SpawnerRuntimeInspector.Inspect(savePath);
            Directory.CreateDirectory(outputFolder);

            WriteJson(
                Path.Combine(outputFolder, "spawner-runtime-report.json"),
                report);

            WriteJson(
                Path.Combine(outputFolder, "spawner-runtime-objects.json"),
                report.Objects);

            WriteJson(
                Path.Combine(outputFolder, "spawner-runtime-guid-references.json"),
                report.GuidReferences);

            WriteJson(
                Path.Combine(outputFolder, "spawner-runtime-functions.json"),
                report.Functions);

            WriteGuidCsv(
                Path.Combine(outputFolder, "spawner-runtime-guid-references.csv"),
                report.GuidReferences);

            WriteAssetCsv(
                Path.Combine(outputFolder, "spawner-runtime-assets.csv"),
                report.Objects);

            WriteMarkdown(
                Path.Combine(outputFolder, "SPAWNER-RUNTIME-NEXT-STEPS.md"),
                report);

            Console.WriteLine(
                $"Objects catalogued:          {report.Summary.TotalObjects}");
            Console.WriteLine(
                $"Objects with Lua:            {report.Summary.ObjectsWithLua}");
            Console.WriteLine(
                $"Objects with custom assets:  {report.Summary.ObjectsWithCustomAssets}");
            Console.WriteLine(
                $"Asset URLs catalogued:       {report.Summary.TotalAssetUrls}");
            Console.WriteLine(
                $"Spawner module characters:   {report.Summary.ModuleCharacters}");
            Console.WriteLine(
                $"Spawner functions:           {report.Summary.FunctionCount}");
            Console.WriteLine(
                $"GUID references:             {report.Summary.GuidReferenceCount}");
            Console.WriteLine(
                $"Resolved GUID references:    {report.Summary.ResolvedGuidCount}");
            Console.WriteLine(
                $"Unresolved GUID references:  {report.Summary.UnresolvedGuidCount}");
            Console.WriteLine();
            Console.WriteLine(
                "This command performs inspection only. " +
                "It does not generate or modify TTS ship objects.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Spawner runtime inspection failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        File.WriteAllText(path, JsonSerializer.Serialize(value, options));
    }

    private static void WriteGuidCsv(
        string path,
        IReadOnlyList<RuntimeGuidReference> references)
    {
        using var writer = new StreamWriter(path);

        writer.WriteLine(
            "Symbol,Guid,ReferenceKind,UsedBySpawnerModule,Resolved," +
            "ObjectName,ObjectNickname,ObjectPath,Assets");

        foreach (var item in references)
        {
            var assets = string.Join(
                " | ",
                item.Assets.Select(
                    x => $"{x.Container}/{x.Role}: {x.Url}"));

            var row = new[]
            {
                item.Symbol,
                item.Guid,
                item.ReferenceKind,
                item.UsedBySpawnerModule.ToString(),
                item.Resolved.ToString(),
                item.ObjectName,
                item.ObjectNickname,
                item.ObjectPath,
                assets
            };

            writer.WriteLine(string.Join(',', row.Select(Csv)));
        }
    }

    private static void WriteAssetCsv(
        string path,
        IReadOnlyList<RuntimeObjectRecord> objects)
    {
        using var writer = new StreamWriter(path);

        writer.WriteLine(
            "Guid,ObjectName,ObjectNickname,ObjectPath,Container,Role,Url");

        foreach (var obj in objects)
        {
            foreach (var asset in obj.Assets)
            {
                var row = new[]
                {
                    obj.Guid,
                    obj.Name,
                    obj.Nickname,
                    obj.Path,
                    asset.Container,
                    asset.Role,
                    asset.Url
                };

                writer.WriteLine(string.Join(',', row.Select(Csv)));
            }
        }
    }

    private static void WriteMarkdown(
        string path,
        SpawnerRuntimeReport report)
    {
        var lines = new List<string>
        {
            "# Unified Spawner Runtime Inspection",
            "",
            "## Purpose",
            "",
            "This report identifies the real TTS objects and asset URLs " +
            "referenced by the Unified ship spawner.",
            "It is an inspection milestone only and does not generate " +
            "First Edition ship objects.",
            "",
            "## Summary",
            "",
            $"- Objects catalogued: {report.Summary.TotalObjects}",
            $"- Objects with custom assets: " +
            $"{report.Summary.ObjectsWithCustomAssets}",
            $"- Asset URLs catalogued: {report.Summary.TotalAssetUrls}",
            $"- Spawner functions: {report.Summary.FunctionCount}",
            $"- GUID references: {report.Summary.GuidReferenceCount}",
            $"- Resolved GUID references: " +
            $"{report.Summary.ResolvedGuidCount}",
            $"- Unresolved GUID references: " +
            $"{report.Summary.UnresolvedGuidCount}",
            "",
            "## Findings",
            ""
        };

        lines.AddRange(report.Findings.Select(x => "- " + x));

        lines.AddRange(new[]
        {
            "",
            "## Next decision",
            "",
            "The next implementation should use only resolved runtime " +
            "source objects and their actual TTS asset URLs.",
            "No asset URL should be guessed or constructed from a " +
            "repository path.",
            ""
        });

        File.WriteAllLines(path, lines);
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string ResolveOutputFolder(
        string[] args,
        string defaultPath)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(
                    "--output",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        return Path.GetFullPath(defaultPath);
    }
}
