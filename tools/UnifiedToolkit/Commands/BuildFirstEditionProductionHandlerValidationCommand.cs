using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionProductionHandlerValidationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot"))
            || string.IsNullOrWhiteSpace(Option(args, "--pilot-card-guid"))
            || string.IsNullOrWhiteSpace(Option(args, "--second-pilot-card-guid")))
        { Usage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]); var sourceSave = Path.GetFullPath(args[1]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!, Ship = Option(args, "--ship"), Faction = Option(args, "--faction")
            };
            var result = new FirstEditionProductionHandlerValidationBuilder().Build(repository, sourceSave, request,
                Option(args, "--pilot-card-guid")!, Option(args, "--second-pilot-card-guid")!,
                Option(args, "--asset-base-url"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "production-handler-validation"));
            Directory.CreateDirectory(output);
            const string stem = "two-ship-validated-production-handlers-v1";
            var savePath = Path.Combine(output, stem + ".json");
            var manifestPath = Path.Combine(output, stem + "-manifest.json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(savePath, result.Save.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(result.Manifest), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R11 Production Handler Validation");
            Console.WriteLine("============================================================"); Console.WriteLine();
            Console.WriteLine($"First Edition pilot:       {result.Manifest.PilotName}");
            Console.WriteLine($"First ship GUID:           {result.Manifest.FirstRegistration.Owner.ShipGuid}");
            Console.WriteLine($"Second ship GUID:          {result.Manifest.SecondRegistration.Owner.ShipGuid}");
            Console.WriteLine($"Upgrade cards registered:  {result.Manifest.FirstRegistration.Upgrades.Count + result.Manifest.SecondRegistration.Upgrades.Count}");
            Console.WriteLine($"Active handlers:           {result.Manifest.ActiveHandlers.Count}");
            Console.WriteLine($"Added shield-token GUID:   {result.Manifest.AddedShieldTokenGuid}");
            Console.WriteLine($"Generated slot:            {result.Manifest.GeneratedEliteSlot?.SlotId}");
            Console.WriteLine($"Acceptance checks passed:  {result.Manifest.AcceptanceChecks.Count(check => check.Passed)}/{result.Manifest.AcceptanceChecks.Count}");
            Console.WriteLine($"Valid:                      {result.Manifest.IsValid}"); Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}"); Console.WriteLine();
            Console.WriteLine("Validated production-handler save prepared. All unreviewed card effects remain inactive.");
            return result.Manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition production-handler validation failed: {exception.Message}"); return 1;
        }
    }
    private static IEnumerable<string> Report(FirstEditionProductionHandlerValidationManifest manifest)
    {
        yield return "# First Edition Validated Production Handlers"; yield return "";
        yield return $"- Pilot: **{manifest.PilotName}**";
        yield return "- Ship A: **Engine Upgrade + R2 Astromech**";
        yield return "- Ship B: **Shield Upgrade + R2-D6 + Predator**";
        yield return "- Predator gameplay: **inactive**";
        yield return $"- Active reviewed handlers: **{manifest.ActiveHandlers.Count}**"; yield return "";
        foreach (var activation in manifest.ActiveHandlers)
            yield return $"- ACTIVE `{activation.UpgradeXws}` → `{activation.MechanicId}`";
        yield return "";
        foreach (var check in manifest.AcceptanceChecks)
            yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
    }
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void Usage() => Console.WriteLine(
        "Usage: UnifiedToolkit build-first-edition-production-handler-validation <repository> <tts-save.json> --pilot <id|name|import-id> --pilot-card-guid <guid> --second-pilot-card-guid <guid>");
}
