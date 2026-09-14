using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionProductionHandlerValidationBuilder
{
    public FirstEditionProductionHandlerValidationResult Build(string repository, string sourceSavePath,
        FirstEditionLoadoutRequest pilotRequest, string firstPilotCardGuid, string secondPilotCardGuid,
        string? assetBaseUrl = null)
    {
        if (firstPilotCardGuid.Equals(secondPilotCardGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The two pilot-card GUIDs must be different.");
        var sourceText = File.ReadAllText(sourceSavePath);
        var source = JsonNode.Parse(sourceText)?.AsObject()
            ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
        var sourceCount = source["ObjectStates"]?.AsArray().Count
            ?? throw new InvalidDataException("The TTS source save has no ObjectStates array.");

        var firstRequest = new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot, Ship = pilotRequest.Ship, Faction = pilotRequest.Faction,
            Upgrades = new() { "engineupgrade", "r2astromech" },
            EnableImplementedStructuralEffects = true
        };
        var first = new FirstEditionProductionLoadoutRegistrar().Register(repository, source, firstRequest,
            firstPilotCardGuid, assetBaseUrl, activateValidatedHandlers: true);

        var secondRequest = new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot, Ship = pilotRequest.Ship, Faction = pilotRequest.Faction,
            Upgrades = new() { "shieldupgrade", "r2d6", "predator" },
            EnableImplementedStructuralEffects = true
        };
        var second = new FirstEditionProductionLoadoutRegistrar().Register(repository, first.Save, secondRequest,
            secondPilotCardGuid, assetBaseUrl, activateValidatedHandlers: true);
        var output = second.Save;
        output["SaveName"] = "Phase 16F-R11 — validated production-handler integration";
        output["Note"] = Append(Text(output, "Note"),
            "Phase 16F-R11 combines all four reviewed production handlers across two legal Red Squadron Pilot loadouts. Predator remains inactive.");

        var index = Index(output);
        var firstShip = index[first.Manifest.Owner.ShipGuid];
        var secondShip = index[second.Manifest.Owner.ShipGuid];
        var firstActions = ShipValues(firstShip, "actSet");
        var firstMoves = ShipValues(firstShip, "moveSet");
        var secondActions = ShipValues(secondShip, "actSet");
        var secondShields = NearbyShieldCount(output, index[secondPilotCardGuid]);
        var firstActivations = first.Manifest.HandlerActivations;
        var secondActivations = second.Manifest.HandlerActivations;
        var allActivations = firstActivations.Concat(secondActivations).ToList();
        var predator = second.Manifest.Upgrades.Single(item => Key(item.Xws) == "predator");
        var generatedElite = second.Blueprint.Slots.SingleOrDefault(slot => slot.Source == "upgrade:r2d6");
        var allGuids = new[] { first.Manifest.Owner.ControllerGuid, second.Manifest.Owner.ControllerGuid }
            .Concat(first.Manifest.Upgrades.Select(item => item.UpgradeCardGuid))
            .Concat(second.Manifest.Upgrades.Select(item => item.UpgradeCardGuid))
            .Concat(second.Manifest.SpawnedResourceTokenGuids).ToList();
        var checks = new List<FirstEditionRuntimeAcceptanceCheck>
        {
            Check("first-production-registration-valid", first.Manifest.IsValid,
                "Engine Upgrade and R2 Astromech production registration passes its internal checks."),
            Check("second-production-registration-valid", second.Manifest.IsValid,
                "Shield Upgrade, R2-D6 and Predator production registration passes its internal checks."),
            Check("four-validated-handlers-active", allActivations.Count == 4
                && allActivations.Select(item => Key(item.UpgradeXws)).ToHashSet().SetEquals(
                    new[] { "engineupgrade", "r2astromech", "r2d6", "shieldupgrade" }),
                "Exactly the four reviewed card-specific handlers are active."),
            Check("engine-upgrade-production-action", firstActions.SequenceEqual(new[] { "F", "TL", "B" }, StringComparer.OrdinalIgnoreCase),
                "The first ship has the First Edition Focus/Target Lock baseline plus Boost."),
            Check("r2-astromech-production-maneuvers", firstMoves.Where(move =>
                    FirstEditionManeuverDifficultyHandler.Speed(move) is 1 or 2)
                .All(move => move.Length > 0 && move[0] == FirstEditionManeuverDifficultyHandler.UnifiedEasyDifficultyCode),
                "Every speed-1 and speed-2 manoeuvre on the first ship uses the easy/green runtime difficulty."),
            Check("second-action-baseline-isolated", secondActions.SequenceEqual(new[] { "F", "TL" }, StringComparer.OrdinalIgnoreCase),
                "The second ship retains the First Edition action baseline without Engine Upgrade's Boost."),
            Check("r2d6-production-slot", generatedElite is not null && generatedElite.Type == "Elite"
                && generatedElite.AssignedUpgradeXws == "predator",
                "R2-D6 generates the Elite slot before Predator is assigned."),
            Check("predator-effect-inactive", predator.ActivationStatus == "inactive"
                && predator.Handlers.All(handler => handler.ActivationStatus == "inactive"),
                "Predator is structurally assigned but its unreviewed gameplay effect remains inactive."),
            Check("shield-upgrade-production-token", second.Manifest.SpawnedResourceTokenGuids.Count == 1
                && secondShields == 3,
                "Shield Upgrade adds one owned physical token to the second ship's two-shield row."),
            Check("owner-isolation", first.Manifest.Owner.ShipGuid != second.Manifest.Owner.ShipGuid
                && first.Manifest.Owner.ControllerGuid != second.Manifest.Owner.ControllerGuid,
                "The two production controllers and owner hierarchies remain isolated."),
            Check("runtime-guids-unique", allGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == allGuids.Count,
                "All controllers, cards and physical-resource tokens have unique GUIDs."),
            Check("bounded-object-additions", output["ObjectStates"]!.AsArray().Count == sourceCount + 8,
                "Two controllers, five upgrade cards and one shield token are the only added objects."),
            Check("source-input-unmodified", source.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString(),
                "The input save remains unmodified in memory.")
        };
        return new FirstEditionProductionHandlerValidationResult
        {
            Save = output,
            Manifest = new FirstEditionProductionHandlerValidationManifest
            {
                PilotName = first.Manifest.RequestedFirstEditionPilot,
                FirstRegistration = first.Manifest,
                SecondRegistration = second.Manifest,
                ActiveHandlers = allActivations,
                GeneratedEliteSlot = generatedElite,
                PredatorCardGuid = predator.UpgradeCardGuid,
                AddedShieldTokenGuid = second.Manifest.SpawnedResourceTokenGuids.Single(),
                AcceptanceChecks = checks
            }
        };
    }

    private static List<string> ShipValues(JsonObject ship, string key)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state)
            || state["shipData"] is not JsonObject data || data[key] is not JsonArray values) return new();
        return values.Select(item => item?.GetValue<string>() ?? "").Where(value => value.Length > 0).ToList();
    }
    private static int NearbyShieldCount(JsonObject save, JsonObject card)
    {
        var px = Number(card, "Transform", "posX"); var pz = Number(card, "Transform", "posZ");
        return save["ObjectStates"]!.AsArray().OfType<JsonObject>().Count(item =>
            Text(item, "Nickname").Equals("Shield", StringComparison.OrdinalIgnoreCase)
            && Distance(Number(item, "Transform", "posX"), Number(item, "Transform", "posZ"), px, pz) <= 4.0);
    }
    private static Dictionary<string, JsonObject> Index(JsonNode root) => Descendants(root)
        .Where(item => Text(item, "GUID").Length > 0).GroupBy(item => Text(item, "GUID"), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        if (node is JsonObject obj) { if (obj["GUID"] is not null) yield return obj; foreach (var pair in obj) if (pair.Value is not null) foreach (var child in Descendants(pair.Value)) yield return child; }
        else if (node is JsonArray array) foreach (var item in array) if (item is not null) foreach (var child in Descendants(item)) yield return child;
    }
    private static bool TryObject(string text, out JsonObject value)
    {
        value = new(); if (text.Length == 0) return false;
        try { if (JsonNode.Parse(text) is not JsonObject parsed) return false; value = parsed; return true; } catch (JsonException) { return false; }
    }
    private static double Number(JsonObject obj, string parent, string key) => obj[parent]?[key]?.GetValue<double>() ?? 0;
    private static double Distance(double x1, double z1, double x2, double z2) => Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(z1 - z2, 2));
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) => existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static string Key(string? value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) => new() { Id = id, Passed = passed, Message = message };
}

public sealed class FirstEditionProductionHandlerValidationResult
{
    public JsonObject Save { get; init; } = new();
    public FirstEditionProductionHandlerValidationManifest Manifest { get; init; } = new();
}

public sealed class FirstEditionProductionHandlerValidationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Only the four separately validated production handlers are active; all other card effects remain inactive.";
    public string PilotName { get; init; } = "";
    public FirstEditionProductionRegistrationManifest FirstRegistration { get; init; } = new();
    public FirstEditionProductionRegistrationManifest SecondRegistration { get; init; } = new();
    public List<FirstEditionProductionHandlerActivation> ActiveHandlers { get; init; } = new();
    public FirstEditionRuntimeSlotContract? GeneratedEliteSlot { get; init; }
    public string PredatorCardGuid { get; init; } = "";
    public string AddedShieldTokenGuid { get; init; } = "";
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}
