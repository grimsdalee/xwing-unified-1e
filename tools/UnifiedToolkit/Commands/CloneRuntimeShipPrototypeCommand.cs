using System.Text;
using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class CloneRuntimeShipPrototypeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: clone-runtime-ship-prototype <runtime-ship-prototype.json> <tts-envelope-save.json> [--output <folder>]");
            return 1;
        }

        var prototypePath = Path.GetFullPath(args[0]);
        var envelopePath = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(Path.GetDirectoryName(prototypePath) ?? ".", "runtime-prototype-clone-r3"));

        if (!File.Exists(prototypePath)) { Console.Error.WriteLine($"Prototype file was not found: {prototypePath}"); return 1; }
        if (!File.Exists(envelopePath)) { Console.Error.WriteLine($"Envelope save was not found: {envelopePath}"); return 1; }

        Console.WriteLine("UnifiedToolkit Phase 6B Revision 3 - Runtime Prototype Clone");
        Console.WriteLine("===========================================================");
        Console.WriteLine();
        Console.WriteLine($"Runtime prototype: {prototypePath}");
        Console.WriteLine($"Envelope save:     {envelopePath}");
        Console.WriteLine($"Output folder:     {output}");
        Console.WriteLine();

        try
        {
            var result = RuntimeShipPrototypeCloner.Clone(prototypePath, envelopePath, output);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "runtime-prototype-clone-report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            WriteGuidCsv(Path.Combine(output, "runtime-prototype-guid-map.csv"), result);
            WriteReport(Path.Combine(output, "RUNTIME-PROTOTYPE-CLONE-REPORT.md"), result);

            Console.WriteLine($"Source ship GUID:             {result.SourceShipGuid}");
            Console.WriteLine($"Cloned ship GUID:             {result.ClonedShipGuid}");
            Console.WriteLine($"Hierarchy objects cloned:     {result.ClonedHierarchyObjectCount}");
            Console.WriteLine($"Fresh GUID mappings:          {result.GuidMappings}");
            Console.WriteLine($"Parent Lua preserved:         {result.ParentScriptPreserved}");
            Console.WriteLine($"Global Lua preserved:         {result.GlobalLuaPreserved}");
            Console.WriteLine($"S-foils trigger found:        {result.SfoilsTriggerFound}");
            Console.WriteLine($"Setup state preserved:        {result.SetupStatePreserved}");
            Console.WriteLine($"finishedSetup forced:         {result.FinishedSetupForced}");
            Console.WriteLine($"S-foils menu text:            {result.SfoilsContextText}");
            Console.WriteLine($"Validation errors:            {result.ValidationErrors.Count}");
            Console.WriteLine($"Ready for TTS load test:      {result.ReadyForTtsLoadTest}");
            Console.WriteLine();
            Console.WriteLine($"Test save: {result.OutputSavePath}");
            Console.WriteLine();
            Console.WriteLine("This clone preserves the original setup-stage state. Gameplay commands appear only after the existing setup flow completes.");
            return result.ReadyForTtsLoadTest ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static void WriteGuidCsv(string path, RuntimePrototypeCloneResult result)
    {
        var lines = new List<string> { "SourceGUID,CloneGUID" };
        lines.AddRange(result.GuidMap.OrderBy(x => x.Key).Select(x => $"\"{x.Key}\",\"{x.Value}\""));
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteReport(string path, RuntimePrototypeCloneResult r)
    {
        var text = new StringBuilder();
        text.AppendLine("# Runtime Prototype Clone Report").AppendLine();
        text.AppendLine($"- Source ship GUID: `{r.SourceShipGuid}`");
        text.AppendLine($"- Cloned ship GUID: `{r.ClonedShipGuid}`");
        text.AppendLine($"- Hierarchy objects cloned: `{r.ClonedHierarchyObjectCount}`");
        text.AppendLine($"- Fresh GUID mappings: `{r.GuidMappings}`");
        text.AppendLine($"- Ready for TTS load test: `{r.ReadyForTtsLoadTest}`");
        text.AppendLine().AppendLine("## Setup lifecycle").AppendLine();
        text.AppendLine($"- Setup state preserved unchanged: `{r.SetupStatePreserved}`");
        text.AppendLine($"- `finishedSetup` forced by cloner: `{r.FinishedSetupForced}`");
        text.AppendLine($"- Source contained `finishedSetup`: `{r.SourceFinishedSetupPresent}`");
        if (r.SourceFinishedSetupPresent)
            text.AppendLine($"- Source `finishedSetup` value: `{r.SourceFinishedSetupValue}`");
        text.AppendLine("- Revision 3 preserves the captured setup-stage state. The existing Unified script remains responsible for deciding when setup is finished and when gameplay context-menu commands become available.");
        text.AppendLine().AppendLine("## S-foils trigger").AppendLine();
        text.AppendLine($"- Existing trigger found: `{r.SfoilsTriggerFound}`");
        text.AppendLine($"- Context-menu text after setup: `{r.SfoilsContextText}`");
        text.AppendLine($"- Function: `{r.SfoilsFunctionName}`");
        text.AppendLine("- The S-foils implementation remains present during setup but is only exposed by the original script after setup completion.");
        if (r.ValidationErrors.Count > 0)
        {
            text.AppendLine().AppendLine("## Validation errors").AppendLine();
            foreach (var error in r.ValidationErrors) text.AppendLine($"- {error}");
        }
        if (r.Notes.Count > 0)
        {
            text.AppendLine().AppendLine("## Notes").AppendLine();
            foreach (var note in r.Notes) text.AppendLine($"- {note}");
        }
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }
}
