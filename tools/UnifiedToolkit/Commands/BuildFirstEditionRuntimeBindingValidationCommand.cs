using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionRuntimeBindingValidationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(Option(args, "--pilot"))) { Usage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!, Ship = Option(args, "--ship"), Faction = Option(args, "--faction"),
                Upgrades = Options(args, "--upgrade").Concat(Options(args, "--upgrades")).ToList()
            };
            var result = new FirstEditionRuntimeBindingValidationBuilder().Build(repository, request, Option(args, "--asset-base-url"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository, "_unifiedtoolkit_reports", "phase16", "runtime-binding-validation"));
            Directory.CreateDirectory(output);
            var stem = Slug(string.IsNullOrWhiteSpace(result.Blueprint.Owner.PilotImportId) ? result.Blueprint.Owner.PilotId : result.Blueprint.Owner.PilotImportId);
            var savePath = Path.Combine(output, stem + ".json");
            var manifestPath = Path.Combine(output, stem + "-manifest.json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(savePath, result.ValidationSave.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(result), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R2 Runtime Binding Validation");
            Console.WriteLine("========================================================");
            Console.WriteLine();
            Console.WriteLine($"Pilot:                    {result.Blueprint.Owner.PilotName}");
            Console.WriteLine($"Ship:                     {result.Blueprint.Owner.ShipName}");
            Console.WriteLine($"Upgrade bindings:         {result.Manifest.Upgrades.Count}");
            Console.WriteLine($"Runtime objects:           {result.Manifest.Upgrades.Count + 4}");
            Console.WriteLine($"Mechanics handlers:        {result.Manifest.Upgrades.Sum(item => item.Handlers.Count)}");
            Console.WriteLine($"Active handlers:           {result.Manifest.Upgrades.Sum(item => item.Handlers.Count(handler => handler.ActivationStatus != "inactive"))}");
            Console.WriteLine($"Acceptance checks passed:  {result.Manifest.AcceptanceChecks.Count(check => check.Passed)}/{result.Manifest.AcceptanceChecks.Count}");
            Console.WriteLine($"Valid:                     {result.Manifest.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Binding fixture completed. Ownership metadata persists across save/load; all gameplay handlers remain inactive.");
            return result.Manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition runtime binding validation failed: {exception.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> Report(FirstEditionRuntimeBindingBuildResult result)
    {
        yield return "# First Edition Runtime Binding Validation";
        yield return "";
        yield return $"- Pilot: **{result.Blueprint.Owner.PilotName}**";
        yield return $"- Ship: **{result.Blueprint.Owner.ShipName}**";
        yield return $"- Ship GUID: `{result.Manifest.Owner.ShipGuid}`";
        yield return $"- Pilot-card GUID: `{result.Manifest.Owner.PilotCardGuid}`";
        yield return $"- Dial GUID: `{result.Manifest.Owner.DialGuid}`";
        yield return $"- Controller GUID: `{result.Manifest.Owner.ControllerGuid}`";
        yield return "- Runtime activation: **inactive**";
        yield return "";
        yield return "## Bound upgrades";
        yield return "";
        yield return "| Upgrade | Slot | Card GUID | Handlers |";
        yield return "|---|---|---|---:|";
        foreach (var item in result.Manifest.Upgrades) yield return $"| {item.Name} | `{item.SlotId}` | `{item.UpgradeCardGuid}` | {item.Handlers.Count} inactive |";
        yield return "";
        yield return "## Acceptance checks";
        yield return "";
        foreach (var check in result.Manifest.AcceptanceChecks) yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
        yield return "";
        yield return "The validation fixture does not modify stats, actions, manoeuvres, tokens, damage, cards or attack resolution.";
    }

    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static IEnumerable<string> Options(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]);
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(new[] { ' ', ':', '/', '\\', '"', '\'', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries));
    private static void Usage() => Console.WriteLine("Usage: UnifiedToolkit build-first-edition-runtime-binding-validation <repository> --pilot <id|name|import-id> [--ship <id>] [--faction <id>] [--upgrade <xws>]... [--asset-base-url <url>] [--output <folder>]");
}
