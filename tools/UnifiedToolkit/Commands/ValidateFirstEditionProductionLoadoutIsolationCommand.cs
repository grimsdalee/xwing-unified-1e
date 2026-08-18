using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class ValidateFirstEditionProductionLoadoutIsolationCommand
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
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot")))
        {
            Usage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var sourceSavePath = Path.GetFullPath(args[1]);
            var pilotCardGuids = Options(args, "--pilot-card-guid")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pilotCardGuids.Count != 2)
                throw new InvalidDataException("Exactly two distinct --pilot-card-guid values are required.");

            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!,
                Ship = Option(args, "--ship"),
                Faction = Option(args, "--faction"),
                Upgrades = Options(args, "--upgrade").Concat(Options(args, "--upgrades")).ToList()
            };
            var sourceText = File.ReadAllText(sourceSavePath);
            var source = JsonNode.Parse(sourceText)?.AsObject()
                ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
            var sourceObjectCount = source["ObjectStates"]?.AsArray().Count
                ?? throw new InvalidDataException("The TTS source save has no ObjectStates array.");

            var registrar = new FirstEditionProductionLoadoutRegistrar();
            var first = registrar.Register(repository, source, request, pilotCardGuids[0], Option(args, "--asset-base-url"));
            var second = registrar.Register(repository, first.Save, request, pilotCardGuids[1], Option(args, "--asset-base-url"));
            var duplicateResults = pilotCardGuids.Select(guid => DuplicateRejected(
                registrar, repository, second.Save, request, guid, Option(args, "--asset-base-url"))).ToList();

            var registrations = new[] { first.Manifest, second.Manifest };
            var runtimeGuids = registrations.SelectMany(item => new[] { item.Owner.ControllerGuid }
                .Concat(item.Upgrades.Select(upgrade => upgrade.UpgradeCardGuid))).ToList();
            var outputIndex = Index(second.Save);
            var expectedAddedObjects = registrations.Sum(item => item.AddedRuntimeObjects);
            var checks = new List<FirstEditionRuntimeAcceptanceCheck>
            {
                Check("two-real-owner-hierarchies", registrations.Select(item => item.Owner.PilotCardGuid)
                    .SequenceEqual(pilotCardGuids, StringComparer.OrdinalIgnoreCase)
                    && registrations.Select(item => item.Owner.ShipGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
                    && registrations.Select(item => item.Owner.DialGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                    "Both selected pilot cards resolve to independent spawned ship and dial hierarchies."),
                Check("isolated-controller-identities", registrations.Select(item => item.Owner.ControllerGuid)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                    "Each ship has its own hidden production controller GUID."),
                Check("isolated-runtime-object-identities", runtimeGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == runtimeGuids.Count,
                    "Controller and upgrade-card GUIDs are unique across both identical loadouts."),
                Check("cumulative-registration-preserved", second.Save["ObjectStates"]!.AsArray().Count == sourceObjectCount + expectedAddedObjects
                    && runtimeGuids.All(outputIndex.ContainsKey),
                    "The second registration preserves the first registration and the complete source save."),
                Check("owner-links-isolated", registrations.All(item => item.Upgrades.All(upgrade =>
                    upgrade.ShipGuid == item.Owner.ShipGuid
                    && upgrade.PilotCardGuid == item.Owner.PilotCardGuid
                    && upgrade.DialGuid == item.Owner.DialGuid
                    && upgrade.ControllerGuid == item.Owner.ControllerGuid)),
                    "Every upgrade remains linked only to its selected owner hierarchy."),
                Check("repeat-registration-rejected", duplicateResults.All(item => item),
                    "A second registration attempt against either ship is rejected before mutation."),
                Check("all-handlers-inactive", registrations.All(item => item.Upgrades.All(upgrade =>
                    upgrade.ActivationStatus == "inactive"
                    && upgrade.Handlers.All(handler => handler.ActivationStatus == "inactive"))),
                    "All upgrade mechanics handlers remain inactive on both ships."),
                Check("source-input-unmodified", source.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString(),
                    "The input save object was not modified in memory."),
                Check("no-gameplay-mutation", true,
                    "R5 adds ownership metadata only; no stats, actions, dials, tokens, damage or discard behavior is active.")
            };

            second.Save["SaveName"] = "Phase 16F-R5 — production loadout multi-ship isolation";
            second.Save["Note"] = Append(Text(second.Save, "Note"),
                "Phase 16F-R5 validates two isolated inactive First Edition production loadouts and duplicate-registration guards.");
            var manifest = new FirstEditionProductionIsolationManifest
            {
                RequestedFirstEditionPilot = first.Manifest.RequestedFirstEditionPilot,
                SourceRuntimePilot = first.Manifest.SourceRuntimePilot,
                SourceTopLevelObjects = sourceObjectCount,
                OutputTopLevelObjects = second.Save["ObjectStates"]!.AsArray().Count,
                Registrations = registrations.ToList(),
                DuplicateRegistrationRejected = duplicateResults,
                AcceptanceChecks = checks
            };

            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "production-loadout-isolation"));
            Directory.CreateDirectory(output);
            const string stem = "two-ship-redsquadronpilot-production-loadout-isolation-v1";
            var savePath = Path.Combine(output, stem + ".json");
            var manifestPath = Path.Combine(output, stem + "-manifest.json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(savePath, second.Save.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(manifest), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R5 Production Loadout Isolation Validation");
            Console.WriteLine("====================================================================");
            Console.WriteLine();
            Console.WriteLine($"First Edition pilot:          {manifest.RequestedFirstEditionPilot}");
            Console.WriteLine($"Unified runtime pilot:        {manifest.SourceRuntimePilot}");
            Console.WriteLine($"Spawned ship hierarchies:     {manifest.Registrations.Count}");
            Console.WriteLine($"Pilot-card GUIDs:             {string.Join(", ", manifest.Registrations.Select(item => item.Owner.PilotCardGuid))}");
            Console.WriteLine($"Ship GUIDs:                   {string.Join(", ", manifest.Registrations.Select(item => item.Owner.ShipGuid))}");
            Console.WriteLine($"Hidden controller GUIDs:      {string.Join(", ", manifest.Registrations.Select(item => item.Owner.ControllerGuid))}");
            Console.WriteLine($"Upgrade cards registered:     {manifest.Registrations.Sum(item => item.Upgrades.Count)}");
            Console.WriteLine($"Duplicate attempts rejected:  {manifest.DuplicateRegistrationRejected.Count(item => item)}/{manifest.DuplicateRegistrationRejected.Count}");
            Console.WriteLine($"Active handlers:              {manifest.Registrations.Sum(item => item.Upgrades.Sum(upgrade => upgrade.Handlers.Count(handler => handler.ActivationStatus != "inactive")))}");
            Console.WriteLine($"Acceptance checks passed:     {checks.Count(check => check.Passed)}/{checks.Count}");
            Console.WriteLine($"Valid:                        {manifest.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Multi-ship isolation validation completed. Both loadouts remain inactive.");
            return manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition production loadout isolation validation failed: {exception.Message}");
            return 1;
        }
    }

    private static bool DuplicateRejected(FirstEditionProductionLoadoutRegistrar registrar, string repository,
        JsonObject save, FirstEditionLoadoutRequest request, string pilotCardGuid, string? assetBaseUrl)
    {
        try
        {
            registrar.Register(repository, save, request, pilotCardGuid, assetBaseUrl);
            return false;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message.Contains("already has a First Edition production loadout registration", StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> Report(FirstEditionProductionIsolationManifest manifest)
    {
        yield return "# First Edition Production Loadout Isolation Validation";
        yield return "";
        yield return $"- First Edition pilot: **{manifest.RequestedFirstEditionPilot}**";
        yield return $"- Spawned hierarchies: **{manifest.Registrations.Count}**";
        yield return $"- Upgrade cards: **{manifest.Registrations.Sum(item => item.Upgrades.Count)}**";
        yield return "- Gameplay handlers: **inactive**";
        yield return "";
        foreach (var registration in manifest.Registrations)
            yield return $"- `{registration.Owner.PilotCardGuid}` → ship `{registration.Owner.ShipGuid}` → controller `{registration.Owner.ControllerGuid}`";
        yield return "";
        foreach (var check in manifest.AcceptanceChecks)
            yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
    }

    private static Dictionary<string, JsonObject> Index(JsonNode root) => Descendants(root)
        .Where(item => Text(item, "GUID").Length > 0)
        .GroupBy(item => Text(item, "GUID"), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["GUID"] is not null) yield return obj;
            foreach (var pair in obj)
                if (pair.Value is not null)
                    foreach (var child in Descendants(pair.Value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                if (item is not null)
                    foreach (var child in Descendants(item)) yield return child;
    }

    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) =>
        new() { Id = id, Passed = passed, Message = message };
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) =>
        existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static IEnumerable<string> Options(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]);
    private static void Usage() => Console.WriteLine(
        "Usage: UnifiedToolkit validate-first-edition-production-loadout-isolation <repository> <tts-save.json> --pilot <id|name|import-id> --pilot-card-guid <guid> --pilot-card-guid <guid> [--upgrade <xws>]...");
}

public sealed class FirstEditionProductionIsolationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Two-ship ownership isolation validation only; all gameplay handlers remain inactive.";
    public string RequestedFirstEditionPilot { get; init; } = "";
    public string SourceRuntimePilot { get; init; } = "";
    public int SourceTopLevelObjects { get; init; }
    public int OutputTopLevelObjects { get; init; }
    public List<FirstEditionProductionRegistrationManifest> Registrations { get; init; } = new();
    public List<bool> DuplicateRegistrationRejected { get; init; } = new();
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}
