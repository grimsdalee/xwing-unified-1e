using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionRuntimeAssignmentBlueprintCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(Option(args, "--pilot")))
        {
            ShowUsage();
            return 1;
        }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!, Ship = Option(args, "--ship"), Faction = Option(args, "--faction"),
                Upgrades = Options(args, "--upgrade").Concat(Options(args, "--upgrades")).ToList()
            };
            var blueprint = new FirstEditionRuntimeAssignmentBlueprintBuilder().Build(repository, request);
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "runtime-assignment-blueprints"));
            Directory.CreateDirectory(output);
            var stem = Slug(string.IsNullOrWhiteSpace(blueprint.Owner.PilotImportId)
                ? blueprint.Owner.PilotId : blueprint.Owner.PilotImportId);
            var jsonPath = Path.Combine(output, stem + ".json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(blueprint, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(blueprint), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R1 Runtime Assignment Blueprint");
            Console.WriteLine("==========================================================");
            Console.WriteLine();
            Console.WriteLine($"Pilot:                    {blueprint.Owner.PilotName}");
            Console.WriteLine($"Ship:                     {blueprint.Owner.ShipName}");
            Console.WriteLine($"Faction:                  {blueprint.Owner.Faction}");
            Console.WriteLine($"Base size:                {blueprint.Owner.BaseSize}");
            Console.WriteLine($"Slot contracts:           {blueprint.Slots.Count}");
            Console.WriteLine($"Upgrade contracts:        {blueprint.Upgrades.Count}");
            Console.WriteLine($"Mechanics handlers:       {blueprint.Upgrades.Sum(item => item.Handlers.Count)}");
            Console.WriteLine($"Active handlers:          {blueprint.Upgrades.Sum(item => item.Handlers.Count(handler => handler.ActivationStatus != "inactive"))}");
            Console.WriteLine($"Acceptance checks passed: {blueprint.AcceptanceChecks.Count(check => check.Passed)}/{blueprint.AcceptanceChecks.Count}");
            Console.WriteLine($"Total squad cost:         {blueprint.Cost.Total}");
            Console.WriteLine($"Valid:                    {blueprint.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"Blueprint: {jsonPath}");
            Console.WriteLine($"Report:    {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Blueprint completed. No TTS save, Lua script, asset or gameplay state was modified.");
            return blueprint.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition runtime assignment blueprint failed: {exception.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> Report(FirstEditionRuntimeAssignmentBlueprint blueprint)
    {
        yield return "# First Edition Runtime Assignment Blueprint";
        yield return "";
        yield return $"- Pilot: **{blueprint.Owner.PilotName}** (`{blueprint.Owner.StablePilotKey}`)";
        yield return $"- Ship: **{blueprint.Owner.ShipName}** (`{blueprint.Owner.StableShipKey}`)";
        yield return $"- Cost: **{blueprint.Cost.Pilot} + {blueprint.Cost.Upgrades} = {blueprint.Cost.Total}**";
        yield return $"- Valid: **{blueprint.IsValid}**";
        yield return "- Runtime activation: **inactive**";
        yield return "";
        yield return "## Upgrade ownership contracts";
        yield return "";
        yield return "| Upgrade | Slot | Points | Handlers | Dependencies |";
        yield return "|---|---|---:|---:|---:|";
        foreach (var upgrade in blueprint.Upgrades)
            yield return $"| {upgrade.Name} (`{upgrade.Xws}`) | `{upgrade.SlotId}` | {upgrade.Points} | {upgrade.Handlers.Count} inactive | {upgrade.Dependencies.Count} unbound |";
        yield return "";
        yield return "## Acceptance checks";
        yield return "";
        foreach (var check in blueprint.AcceptanceChecks)
            yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
        yield return "";
        yield return "No gameplay effect, TTS GUID or Lua handler is activated by this blueprint.";
    }

    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static IEnumerable<string> Options(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]);
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(
        new[] { ' ', ':', '/', '\\', '"', '\'', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries));
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit build-first-edition-runtime-assignment-blueprint <repository> --pilot <id|name|import-id> [--ship <id>] [--faction <id>] [--upgrade <xws>]... [--upgrades <xws,xws>] [--output <folder>]");
}
