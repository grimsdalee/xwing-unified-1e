using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class RegisterFirstEditionProductionLoadoutCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot"))) { Usage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]); var sourceSave = Path.GetFullPath(args[1]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!, Ship = Option(args, "--ship"), Faction = Option(args, "--faction"),
                Upgrades = Options(args, "--upgrade").Concat(Options(args, "--upgrades")).ToList()
            };
            var source = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(sourceSave))?.AsObject()
                ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
            var result = new FirstEditionProductionLoadoutRegistrar().Register(repository, source, request,
                Option(args, "--pilot-card-guid"), Option(args, "--asset-base-url"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "production-loadout-registration"));
            Directory.CreateDirectory(output);
            var stem = Slug(string.IsNullOrWhiteSpace(result.Blueprint.Owner.PilotImportId)
                ? result.Blueprint.Owner.PilotId : result.Blueprint.Owner.PilotImportId);
            var savePath = Path.Combine(output, stem + ".json");
            var manifestPath = Path.Combine(output, stem + "-manifest.json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(savePath, result.Save.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(result), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R4 Production Loadout Registration");
            Console.WriteLine("============================================================"); Console.WriteLine();
            Console.WriteLine($"First Edition pilot:       {result.Manifest.RequestedFirstEditionPilot}");
            Console.WriteLine($"Unified runtime pilot:     {result.Manifest.SourceRuntimePilot}");
            Console.WriteLine($"Pilot-card GUID:           {result.Manifest.Owner.PilotCardGuid}");
            Console.WriteLine($"Ship GUID:                 {result.Manifest.Owner.ShipGuid}");
            Console.WriteLine($"Dial GUID:                 {result.Manifest.Owner.DialGuid}");
            Console.WriteLine($"Hidden controller GUID:    {result.Manifest.Owner.ControllerGuid}");
            Console.WriteLine($"Source objects preserved:  {result.Manifest.SourceTopLevelObjects}");
            Console.WriteLine($"Upgrade cards registered:  {result.Manifest.Upgrades.Count}");
            Console.WriteLine($"Active handlers:           {result.Manifest.Upgrades.Sum(item => item.Handlers.Count(handler => handler.ActivationStatus != "inactive"))}");
            Console.WriteLine($"Acceptance checks passed:  {result.Manifest.AcceptanceChecks.Count(check => check.Passed)}/{result.Manifest.AcceptanceChecks.Count}");
            Console.WriteLine($"Valid:                     {result.Manifest.IsValid}"); Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}"); Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}"); Console.WriteLine();
            Console.WriteLine("Production registration completed. The controller is hidden and every gameplay handler remains inactive.");
            return result.Manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition production loadout registration failed: {exception.Message}"); return 1;
        }
    }
    private static IEnumerable<string> Report(FirstEditionProductionRegistrationResult result)
    {
        yield return "# First Edition Production Loadout Registration"; yield return "";
        yield return $"- First Edition pilot: **{result.Manifest.RequestedFirstEditionPilot}**";
        yield return $"- Unified donor: **{result.Manifest.SourceRuntimePilot}**";
        yield return $"- Ship GUID: `{result.Manifest.Owner.ShipGuid}`";
        yield return $"- Hidden controller GUID: `{result.Manifest.Owner.ControllerGuid}`";
        yield return "- Gameplay handlers: **inactive**"; yield return "";
        foreach (var check in result.Manifest.AcceptanceChecks) yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
    }
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static IEnumerable<string> Options(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]);
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(new[] { ' ', ':', '/', '\\', '"', '\'', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries));
    private static void Usage() => Console.WriteLine("Usage: UnifiedToolkit register-first-edition-production-loadout <repository> <tts-save.json> --pilot <id|name|import-id> [--pilot-card-guid <guid>] [--upgrade <xws>]...");
}
