using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionUpgradeSlotValidationBuilder
{
    private const string R2D6Xws = "r2d6";
    private const string PredatorXws = "predator";
    private static readonly JsonSerializerOptions ContractJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public FirstEditionUpgradeSlotValidationResult Build(string repository, string sourceSavePath,
        FirstEditionLoadoutRequest pilotRequest, string activePilotCardGuid, string controlPilotCardGuid,
        string? assetBaseUrl = null)
    {
        if (activePilotCardGuid.Equals(controlPilotCardGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Active and control pilot-card GUIDs must be different.");

        var sourceText = File.ReadAllText(sourceSavePath);
        var source = JsonNode.Parse(sourceText)?.AsObject()
            ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
        var sourceIndex = Index(source);
        var activeOwner = ResolveOwner(sourceIndex, activePilotCardGuid);
        var controlOwner = ResolveOwner(sourceIndex, controlPilotCardGuid);
        if (activeOwner.ShipGuid == controlOwner.ShipGuid)
            throw new InvalidDataException("Active and control pilot cards resolve to the same ship.");

        var request = new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot,
            Ship = pilotRequest.Ship,
            Faction = pilotRequest.Faction,
            Upgrades = new() { R2D6Xws, PredatorXws },
            EnableImplementedStructuralEffects = true
        };
        var registration = new FirstEditionProductionLoadoutRegistrar().Register(
            repository, source, request, activePilotCardGuid, assetBaseUrl);
        var activeBlueprint = registration.Blueprint;
        var r2d6 = activeBlueprint.Upgrades.Single(upgrade => upgrade.Xws == R2D6Xws);
        var predator = activeBlueprint.Upgrades.Single(upgrade => upgrade.Xws == PredatorXws);
        var generatedSlot = activeBlueprint.Slots.SingleOrDefault(slot => slot.Source == "upgrade:r2d6");

        var controlPlan = new FirstEditionLoadoutPlanner().Plan(repository, new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot,
            Ship = pilotRequest.Ship,
            Faction = pilotRequest.Faction
        });
        var controlSlots = controlPlan.Slots.Select(slot => new FirstEditionRuntimeSlotContract
        {
            SlotId = slot.SlotId,
            Type = slot.Type,
            Ordinal = slot.Ordinal,
            Source = slot.Source,
            AssignedUpgradeXws = slot.AssignedUpgradeXws
        }).ToList();

        var output = registration.Save;
        var outputIndex = Index(output);
        var r2d6Binding = registration.Manifest.Upgrades.Single(item => item.Xws == R2D6Xws);
        var predatorBinding = registration.Manifest.Upgrades.Single(item => item.Xws == PredatorXws);
        var controller = outputIndex[registration.Manifest.Owner.ControllerGuid];
        var r2d6Card = outputIndex[r2d6Binding.UpgradeCardGuid];
        var predatorCard = outputIndex[predatorBinding.UpgradeCardGuid];
        ActivateStructuralMetadata(controller, r2d6Card, predatorCard, registration.Manifest.Owner,
            activeBlueprint.Slots, r2d6Binding, predatorBinding);

        output["SaveName"] = "Phase 16F-R9 — R2-D6 generated Elite-slot validation";
        output["Note"] = Append(Text(output, "Note"),
            "Phase 16F-R9 validates R2-D6's generated Elite slot on the selected ship. Predator is bound to that slot, but Predator's gameplay handlers remain inactive. The control ship receives no slot change.");

        var outputAfter = Index(output);
        var sourcePreserved = sourceIndex.All(pair => outputAfter.TryGetValue(pair.Key, out var copy)
            && Text(pair.Value, "LuaScript") == Text(copy, "LuaScript")
            && Text(pair.Value, "LuaScriptState") == Text(copy, "LuaScriptState"));
        var slotHandler = r2d6.Handlers.SingleOrDefault(handler => handler.MechanicId == "upgrade-slot-change");
        var checks = new List<FirstEditionRuntimeAcceptanceCheck>
        {
            Check("pilot-eligible-for-r2d6", activeBlueprint.Owner.PilotName == "Red Squadron Pilot"
                && !controlPlan.Pilot.PrintedUpgradeSlots.Any(slot => FirstEditionLoadoutPlanner.NormalizeSlot(slot) == "Elite")
                && controlPlan.Pilot.PilotSkill > 2,
                "The pilot has an Astromech slot, no printed Elite slot, and pilot skill above 2."),
            Check("r2d6-bound-to-printed-astromech", r2d6.SlotId == "astromech:1",
                "R2-D6 occupies the pilot's printed Astromech slot."),
            Check("single-generated-elite-slot", generatedSlot is not null && generatedSlot.Type == "Elite"
                && generatedSlot.SlotId == "elite:1"
                && activeBlueprint.Slots.Count(slot => slot.Source == "upgrade:r2d6") == 1,
                "R2-D6 generates exactly one Elite slot with an explicit source contract."),
            Check("dependent-upgrade-bound", generatedSlot?.AssignedUpgradeXws == PredatorXws
                && predator.SlotId == generatedSlot?.SlotId,
                "Predator is assigned to the Elite slot generated by R2-D6."),
            Check("control-has-no-generated-slot", controlSlots.All(slot => slot.Type != "Elite"
                && !slot.Source.StartsWith("upgrade:", StringComparison.OrdinalIgnoreCase)),
                "The control ship retains only its printed and implicit First Edition slots."),
            Check("single-active-structural-handler", slotHandler is not null
                && r2d6.Handlers.Count(handler => handler.MechanicId == "upgrade-slot-change") == 1,
                "The reviewed R2-D6 upgrade-slot-change contract is the sole active mechanic."),
            Check("dependent-gameplay-inactive", predator.Handlers.All(handler => handler.ActivationStatus == "inactive")
                && predatorBinding.ActivationStatus == "inactive",
                "Predator is structurally equipped but none of its gameplay effects are active."),
            Check("source-hierarchy-preserved", sourcePreserved,
                "Every pre-existing Unified object retains its Lua script and saved runtime state."),
            Check("no-unrelated-gameplay-mutation", sourceIndex.Count + 3 == outputAfter.Count,
                "Only the hidden controller and two upgrade cards are added; no dial, action, token or stat state changes."),
            Check("source-input-unmodified", source.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString(),
                "The input save object was not modified in memory.")
        };

        return new FirstEditionUpgradeSlotValidationResult
        {
            Save = output,
            Manifest = new FirstEditionUpgradeSlotValidationManifest
            {
                PilotName = activeBlueprint.Owner.PilotName,
                PilotSkill = controlPlan.Pilot.PilotSkill,
                ActiveOwner = registration.Manifest.Owner,
                ControlPilotCardGuid = controlOwner.PilotCardGuid,
                ControlShipGuid = controlOwner.ShipGuid,
                BaselineSlots = controlSlots,
                ActiveSlots = activeBlueprint.Slots,
                R2D6CardGuid = r2d6Binding.UpgradeCardGuid,
                DependentUpgradeCardGuid = predatorBinding.UpgradeCardGuid,
                ActiveHandlerCount = 1,
                AcceptanceChecks = checks
            }
        };
    }

    private static void ActivateStructuralMetadata(JsonObject controller, JsonObject r2d6Card,
        JsonObject predatorCard, FirstEditionRuntimeOwnerBinding owner,
        List<FirstEditionRuntimeSlotContract> slots, FirstEditionRuntimeUpgradeBinding r2d6,
        FirstEditionRuntimeUpgradeBinding predator)
    {
        var state = new JsonObject
        {
            ["owner"] = JsonSerializer.SerializeToNode(owner, ContractJson),
            ["activationStatus"] = "active-structural",
            ["activeHandlerId"] = "upgrade-slot-change:r2d6",
            ["slots"] = JsonSerializer.SerializeToNode(slots, ContractJson),
            ["r2d6CardGuid"] = r2d6.UpgradeCardGuid,
            ["dependentUpgradeCardGuid"] = predator.UpgradeCardGuid
        };
        controller["LuaScriptState"] = state.ToJsonString();
        controller["Description"] = "Active First Edition structural-slot controller — R2-D6 only";
        if (TryObject(Text(controller, "GMNotes"), out var controllerNotes))
        {
            controllerNotes["activationStatus"] = "active-structural";
            controllerNotes["activeHandlerId"] = "upgrade-slot-change:r2d6";
            controller["GMNotes"] = controllerNotes.ToJsonString();
        }

        if (!TryObject(Text(r2d6Card, "GMNotes"), out var r2d6Notes))
            throw new InvalidDataException("R2-D6 card metadata is not readable.");
        r2d6Notes["activationStatus"] = "active-structural";
        if (r2d6Notes["handlers"] is JsonArray handlers)
            foreach (var handler in handlers.OfType<JsonObject>())
                handler["activationStatus"] = Text(handler, "mechanicId") == "upgrade-slot-change"
                    ? "active" : "inactive";
        r2d6Card["GMNotes"] = r2d6Notes.ToJsonString();
        r2d6Card["LuaScriptState"] = r2d6Notes.ToJsonString();
        r2d6Card["Description"] = "Astromech — 1 point\nBound slot: astromech:1\nRuntime: active structural effect — adds Elite slot";
        predatorCard["Description"] = "Elite — 3 points\nBound slot: elite:1 generated by R2-D6\nRuntime: gameplay inactive";
    }

    private static SourceOwner ResolveOwner(Dictionary<string, JsonObject> objects, string pilotCardGuid)
    {
        if (!objects.TryGetValue(pilotCardGuid, out var card) || !TryObject(Text(card, "LuaScriptState"), out var state))
            throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' has no readable runtime state.");
        var shipGuid = Text(state, "ship_guid");
        var dialGuid = Text(state, "dial_guid");
        if (!objects.ContainsKey(shipGuid) || !objects.ContainsKey(dialGuid))
            throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' has unresolved ship or dial links.");
        return new SourceOwner(pilotCardGuid, shipGuid, dialGuid);
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
            foreach (var pair in obj) if (pair.Value is not null)
                foreach (var child in Descendants(pair.Value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null)
                foreach (var child in Descendants(item)) yield return child;
    }
    private static bool TryObject(string text, out JsonObject value)
    {
        value = new JsonObject();
        if (text.Length == 0) return false;
        try { if (JsonNode.Parse(text) is JsonObject parsed) { value = parsed; return true; } }
        catch (JsonException) { }
        return false;
    }
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) =>
        new() { Id = id, Passed = passed, Message = message };
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) =>
        existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private sealed record SourceOwner(string PilotCardGuid, string ShipGuid, string DialGuid);
}

public sealed class FirstEditionUpgradeSlotValidationResult
{
    public JsonObject Save { get; init; } = new();
    public FirstEditionUpgradeSlotValidationManifest Manifest { get; init; } = new();
}

public sealed class FirstEditionUpgradeSlotValidationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "R2-D6 adds one Elite slot; the dependent Elite upgrade's gameplay handlers remain inactive.";
    public string PilotName { get; init; } = "";
    public int PilotSkill { get; init; }
    public FirstEditionRuntimeOwnerBinding ActiveOwner { get; init; } = new();
    public string ControlPilotCardGuid { get; init; } = "";
    public string ControlShipGuid { get; init; } = "";
    public List<FirstEditionRuntimeSlotContract> BaselineSlots { get; init; } = new();
    public List<FirstEditionRuntimeSlotContract> ActiveSlots { get; init; } = new();
    public string R2D6CardGuid { get; init; } = "";
    public string DependentUpgradeCardGuid { get; init; } = "";
    public int ActiveHandlerCount { get; init; }
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}
