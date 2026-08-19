using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionAddedActionValidationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot"))
            || string.IsNullOrWhiteSpace(Option(args, "--pilot-card-guid"))
            || string.IsNullOrWhiteSpace(Option(args, "--control-pilot-card-guid")))
        {
            Usage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var sourceSave = Path.GetFullPath(args[1]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!,
                Ship = Option(args, "--ship"),
                Faction = Option(args, "--faction")
            };
            var result = new FirstEditionAddedActionValidationBuilder().Build(
                repository,
                sourceSave,
                request,
                Option(args, "--pilot-card-guid")!,
                Option(args, "--control-pilot-card-guid")!,
                Option(args, "--asset-base-url"));

            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "added-action-validation"));
            Directory.CreateDirectory(output);
            const string stem = "redsquadronpilot-engine-upgrade-added-action-v1";
            var savePath = Path.Combine(output, stem + ".json");
            var manifestPath = Path.Combine(output, stem + "-manifest.json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(savePath, result.Save.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(result.Manifest), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R7 Added Action Validation");
            Console.WriteLine("====================================================");
            Console.WriteLine();
            Console.WriteLine($"First Edition pilot:       {result.Manifest.PilotName}");
            Console.WriteLine($"Active ship GUID:          {result.Manifest.ActiveOwner.ShipGuid}");
            Console.WriteLine($"Control ship GUID:         {result.Manifest.ControlOwner.ShipGuid}");
            Console.WriteLine($"First Edition baseline:    {string.Join(", ", result.Manifest.BaselineActionCodes)}");
            Console.WriteLine($"Added action:              {result.Manifest.AddedActionName} ({result.Manifest.AddedActionCode})");
            Console.WriteLine($"Active effective actions: {string.Join(", ", result.Manifest.ActiveEffectiveActionCodes)}");
            Console.WriteLine($"Control effective actions:{string.Join(", ", result.Manifest.ControlEffectiveActionCodes)}");
            Console.WriteLine($"Active handlers:          {result.Manifest.ActiveHandlerCount}");
            Console.WriteLine($"Acceptance checks passed: {result.Manifest.AcceptanceChecks.Count(check => check.Passed)}/{result.Manifest.AcceptanceChecks.Count}");
            Console.WriteLine($"Valid:                    {result.Manifest.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Added-action validation prepared. Engine Upgrade is the only active upgrade handler.");
            return result.Manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition added-action validation failed: {exception.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> Report(FirstEditionAddedActionValidationManifest manifest)
    {
        yield return "# First Edition Added Action Validation";
        yield return "";
        yield return $"- Pilot: **{manifest.PilotName}**";
        yield return $"- Active ship: `{manifest.ActiveOwner.ShipGuid}`";
        yield return $"- Control ship: `{manifest.ControlOwner.ShipGuid}`";
        yield return $"- First Edition baseline: `{string.Join(", ", manifest.BaselineActionCodes)}`";
        yield return $"- Engine Upgrade result: `{string.Join(", ", manifest.ActiveEffectiveActionCodes)}`";
        yield return $"- Control result: `{string.Join(", ", manifest.ControlEffectiveActionCodes)}`";
        yield return "- Printed stat changes: **none**";
        yield return "";
        foreach (var check in manifest.AcceptanceChecks)
            yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
    }

    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static void Usage() => Console.WriteLine(
        "Usage: UnifiedToolkit build-first-edition-added-action-validation <repository> <tts-save.json> --pilot <id|name|import-id> --pilot-card-guid <guid> --control-pilot-card-guid <guid> [--output <folder>]");
}
