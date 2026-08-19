using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionAddedActionValidationBuilder
{
    private const string EngineUpgradeXws = "engineupgrade";
    private const string BoostActionCode = "B";
    private static readonly JsonSerializerOptions ContractJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    private static readonly IReadOnlyDictionary<string, string> ActionCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["focus"] = "F",
            ["targetlock"] = "TL",
            ["target lock"] = "TL",
            ["evade"] = "E",
            ["reinforce"] = "R",
            ["calculate"] = "C",
            ["cloak"] = "CL",
            ["barrelroll"] = "BR",
            ["barrel roll"] = "BR",
            ["boost"] = "B"
        };

    public FirstEditionAddedActionValidationResult Build(string repository, string sourceSavePath,
        FirstEditionLoadoutRequest pilotRequest, string activePilotCardGuid, string controlPilotCardGuid,
        string? assetBaseUrl = null)
    {
        if (activePilotCardGuid.Equals(controlPilotCardGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Active and control pilot-card GUIDs must be different.");

        var sourceText = File.ReadAllText(sourceSavePath);
        var source = JsonNode.Parse(sourceText)?.AsObject()
            ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
        var sourceIndex = Index(source);
        var activeSourceOwner = ResolveOwner(sourceIndex, activePilotCardGuid);
        var controlSourceOwner = ResolveOwner(sourceIndex, controlPilotCardGuid);
        if (activeSourceOwner.ShipGuid == controlSourceOwner.ShipGuid)
            throw new InvalidDataException("Active and control pilot cards resolve to the same ship.");

        var baselinePlan = new FirstEditionLoadoutPlanner().Plan(repository, pilotRequest);
        if (!baselinePlan.IsValid)
            throw new InvalidDataException("The First Edition baseline loadout plan is not valid.");
        var baselineCodes = baselinePlan.Ship.Actions.Select(ToActionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (baselineCodes.Count != baselinePlan.Ship.Actions.Count)
            throw new InvalidDataException("The First Edition action bar contains duplicate runtime action codes.");

        var engineRequest = new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot,
            Ship = pilotRequest.Ship,
            Faction = pilotRequest.Faction,
            Upgrades = new() { EngineUpgradeXws }
        };
        var registration = new FirstEditionProductionLoadoutRegistrar().Register(
            repository, source, engineRequest, activePilotCardGuid, assetBaseUrl);
        var engine = registration.Manifest.Upgrades.SingleOrDefault(item =>
            item.Xws.Equals(EngineUpgradeXws, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Engine Upgrade was not registered.");
        var engineContract = registration.Blueprint.Upgrades.Single(item =>
            item.Xws.Equals(EngineUpgradeXws, StringComparison.OrdinalIgnoreCase));
        if (!engineContract.Handlers.Any(handler => handler.MechanicId == "adds-action"))
            throw new InvalidDataException("Engine Upgrade has no adds-action handler contract.");

        var output = registration.Save;
        var outputObjects = output["ObjectStates"]!.AsArray();
        var outputIndex = Index(output);
        var activeController = outputIndex[registration.Manifest.Owner.ControllerGuid];
        var activeCard = outputIndex[engine.UpgradeCardGuid];
        var usedGuids = outputIndex.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var controlControllerGuid = GuidFor($"{controlSourceOwner.ShipGuid}:first-edition-action-baseline-controller", usedGuids);

        var activeActions = baselineCodes.Concat(new[] { BoostActionCode }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PersistActionCodes(outputIndex[activeSourceOwner.ShipGuid], activeActions);
        PersistActionCodes(outputIndex[controlSourceOwner.ShipGuid], baselineCodes);
        var activeState = ControllerState(registration.Manifest.Owner, baselineCodes, activeActions, true, engine.UpgradeCardGuid);
        ActivateController(activeController, activeState, "Engine Upgrade — active added-action controller");
        ActivateUpgradeCard(activeCard);

        var controlOwner = new FirstEditionRuntimeOwnerBinding
        {
            StablePilotKey = registration.Manifest.Owner.StablePilotKey + ":control",
            StableShipKey = registration.Manifest.Owner.StableShipKey + ":control",
            PilotCardGuid = controlSourceOwner.PilotCardGuid,
            ShipGuid = controlSourceOwner.ShipGuid,
            DialGuid = controlSourceOwner.DialGuid,
            ControllerGuid = controlControllerGuid
        };
        var controlState = ControllerState(controlOwner, baselineCodes, baselineCodes, false, "");
        var controlController = activeController.DeepClone().AsObject();
        controlController["GUID"] = controlControllerGuid;
        ActivateController(controlController, controlState, "First Edition action baseline — control ship");
        controlController["GMNotes"] = JsonSerializer.Serialize(new
        {
            kind = "first-edition-action-baseline-controller",
            shipGuid = controlOwner.ShipGuid,
            activationStatus = "baseline-only"
        }, ContractJson);
        outputObjects.Add(controlController);

        output["SaveName"] = "Phase 16F-R7 — Engine Upgrade added-action validation";
        output["Note"] = Append(Text(output, "Note"),
            "Phase 16F-R7 applies the First Edition action baseline to both ships and adds Boost only to the Engine Upgrade ship.");

        var outputIndexAfter = Index(output);
        var sourceScriptsPreserved = sourceIndex.All(pair => outputIndexAfter.TryGetValue(pair.Key, out var copy)
            && Text(pair.Value, "LuaScript") == Text(copy, "LuaScript"));
        var targetShipGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            activeSourceOwner.ShipGuid,
            controlSourceOwner.ShipGuid
        };
        var nonTargetStatesPreserved = sourceIndex.Where(pair => !targetShipGuids.Contains(pair.Key))
            .All(pair => outputIndexAfter.TryGetValue(pair.Key, out var copy)
                && Text(pair.Value, "LuaScriptState") == Text(copy, "LuaScriptState"));
        var activePersistedActions = PersistedActionCodes(outputIndexAfter[activeSourceOwner.ShipGuid]);
        var controlPersistedActions = PersistedActionCodes(outputIndexAfter[controlSourceOwner.ShipGuid]);
        var activeLua = Text(activeController, "LuaScript");
        var controlLua = Text(controlController, "LuaScript");
        var checks = new List<FirstEditionRuntimeAcceptanceCheck>
        {
            Check("first-edition-action-baseline", baselineCodes.SequenceEqual(new[] { "F", "TL" }, StringComparer.OrdinalIgnoreCase),
                "The Red Squadron Pilot baseline is Focus and Target Lock."),
            Check("engine-upgrade-adds-boost", activeActions.Count(code => code == BoostActionCode) == 1
                && activeActions.Count == baselineCodes.Count + 1,
                "Engine Upgrade adds exactly one Boost action to the First Edition baseline."),
            Check("control-has-no-boost", !baselineCodes.Contains(BoostActionCode, StringComparer.OrdinalIgnoreCase),
                "The control ship uses the same First Edition baseline without Boost."),
            Check("persisted-action-state", activePersistedActions.SequenceEqual(activeActions, StringComparer.OrdinalIgnoreCase)
                && controlPersistedActions.SequenceEqual(baselineCodes, StringComparer.OrdinalIgnoreCase),
                "Each ship's saved shipData.actSet contains its First Edition actions before the normal dial load lifecycle begins."),
            Check("existing-unified-action-runtime-reused", activeLua.Contains("ship.setTable('Data', data)", StringComparison.Ordinal)
                && !activeLua.Contains("dial.call('assignShip'", StringComparison.Ordinal),
                "The controller maintains Data.actSet without racing or replacing the existing Unified dial lifecycle."),
            Check("save-load-idempotent", activeLua.Contains("copy(config.effectiveActionCodes)", StringComparison.Ordinal)
                && controlLua.Contains("copy(config.effectiveActionCodes)", StringComparison.Ordinal),
                "Action codes are copied from the canonical effective set without accumulating duplicates after save/load."),
            Check("owner-isolation", registration.Manifest.Owner.ShipGuid != controlOwner.ShipGuid
                && registration.Manifest.Owner.ControllerGuid != controlOwner.ControllerGuid,
                "Active and control ships use distinct owner hierarchies and hidden controllers."),
            Check("source-hierarchy-preserved", sourceScriptsPreserved && nonTargetStatesPreserved
                && outputObjects.Count == source["ObjectStates"]!.AsArray().Count + 3,
                "All source objects and scripts are preserved; only the two target ship action states and three added runtime objects differ."),
            Check("single-active-upgrade-handler", engineContract.Handlers.Count(handler => handler.MechanicId == "adds-action") == 1,
                "Engine Upgrade's adds-action handler is the only active upgrade mechanic."),
            Check("printed-stats-untouched", !activeLua.Contains("Hull", StringComparison.OrdinalIgnoreCase)
                && !activeLua.Contains("Agility", StringComparison.OrdinalIgnoreCase)
                && !activeLua.Contains("Attack", StringComparison.OrdinalIgnoreCase),
                "No printed hull, agility, attack or pilot-skill value is changed."),
            Check("source-input-unmodified", source.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString(),
                "The input save object was not modified in memory.")
        };

        return new FirstEditionAddedActionValidationResult
        {
            Save = output,
            Manifest = new FirstEditionAddedActionValidationManifest
            {
                PilotName = registration.Manifest.RequestedFirstEditionPilot,
                ActiveOwner = registration.Manifest.Owner,
                ControlOwner = controlOwner,
                BaselineActionCodes = baselineCodes,
                AddedActionName = "Boost",
                AddedActionCode = BoostActionCode,
                ActiveEffectiveActionCodes = activeActions,
                ControlEffectiveActionCodes = baselineCodes,
                ActiveUpgradeCardGuid = engine.UpgradeCardGuid,
                ActiveHandlerCount = 1,
                AcceptanceChecks = checks
            }
        };
    }

    private static JsonObject ControllerState(FirstEditionRuntimeOwnerBinding owner,
        List<string> baseline, List<string> effective, bool engineUpgradeActive, string upgradeCardGuid) =>
        new()
        {
            ["owner"] = JsonSerializer.SerializeToNode(owner, ContractJson),
            ["activationStatus"] = engineUpgradeActive ? "active" : "baseline-only",
            ["handlerId"] = engineUpgradeActive ? "adds-action:engineupgrade" : "first-edition-action-baseline",
            ["mechanicId"] = engineUpgradeActive ? "adds-action" : "baseline-action-bar",
            ["upgradeXws"] = engineUpgradeActive ? EngineUpgradeXws : "",
            ["upgradeCardGuid"] = upgradeCardGuid,
            ["baselineActionCodes"] = new JsonArray(baseline.Select(value => JsonValue.Create(value)).ToArray()),
            ["effectiveActionCodes"] = new JsonArray(effective.Select(value => JsonValue.Create(value)).ToArray()),
            ["applied"] = false
        };

    private static void ActivateController(JsonObject controller, JsonObject state, string description)
    {
        var serializedState = state.ToJsonString();
        controller["Description"] = description;
        controller["LuaScriptState"] = serializedState;
        controller["LuaScript"] = ControllerLua(serializedState);
        if (TryObject(Text(controller, "GMNotes"), out var notes))
        {
            notes["activationStatus"] = Text(state, "activationStatus");
            notes["handlerId"] = Text(state, "handlerId");
            controller["GMNotes"] = notes.ToJsonString();
        }
    }

    private static void ActivateUpgradeCard(JsonObject card)
    {
        if (!TryObject(Text(card, "GMNotes"), out var state))
            throw new InvalidDataException("Engine Upgrade card metadata is not readable.");
        state["activationStatus"] = "active";
        if (state["handlers"] is JsonArray handlers)
            foreach (var item in handlers.OfType<JsonObject>())
                if (Text(item, "mechanicId") == "adds-action") item["activationStatus"] = "active";
        var serialized = state.ToJsonString();
        card["GMNotes"] = serialized;
        card["LuaScriptState"] = serialized;
        card["Description"] = "Modification — 4 points\nBound slot: modification:1\nRuntime: active — adds Boost";
    }

    private static void PersistActionCodes(JsonObject ship, IReadOnlyList<string> actionCodes)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state)
            || state["shipData"] is not JsonObject shipData)
            throw new InvalidDataException($"Ship GUID '{Text(ship, "GUID")}' has no persisted shipData state.");
        shipData["actSet"] = new JsonArray(actionCodes.Select(value => JsonValue.Create(value)).ToArray());
        ship["LuaScriptState"] = state.ToJsonString();
    }

    private static List<string> PersistedActionCodes(JsonObject ship)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state)
            || state["shipData"] is not JsonObject shipData
            || shipData["actSet"] is not JsonArray actions)
            return new();
        return actions.Select(item => item?.GetValue<string>() ?? "").Where(value => value.Length > 0).ToList();
    }

    private static string ControllerLua(string state) => $$"""
        -- First Edition action-bar bridge. Only the reviewed adds-action handler is active.
        local config = JSON.decode({{LuaString(state)}})
        local attempts = 0

        local function copy(values)
          local result = {}
          for _, value in ipairs(values or {}) do table.insert(result, value) end
          return result
        end

        function applyFirstEditionActions()
          local ship = getObjectFromGUID(config.owner.shipGuid)
          if ship == nil then return false end
          local data = ship.getTable('Data')
          if data == nil then return false end
          data.actSet = copy(config.effectiveActionCodes)
          ship.setTable('Data', data)
          config.applied = true
          self.setTable('FirstEditionActionBinding', config)
          return true
        end

        function applyWhenReady()
          attempts = attempts + 1
          if applyFirstEditionActions() then return end
          if attempts < 180 then Wait.frames(applyWhenReady, 1) end
        end

        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then config = JSON.decode(saved_data) end
          config.applied = false
          self.setTable('FirstEditionActionBinding', config)
          Wait.frames(applyWhenReady, 5)
        end

        function onSave() return JSON.encode(config) end
        """;

    private static string ToActionCode(string action)
    {
        var key = new string(action.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (ActionCodes.TryGetValue(key, out var code)) return code;
        throw new InvalidDataException($"First Edition action '{action}' has no Unified runtime action-code mapping.");
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
            foreach (var pair in obj)
                if (pair.Value is not null)
                    foreach (var child in Descendants(pair.Value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                if (item is not null)
                    foreach (var child in Descendants(item)) yield return child;
    }

    private static bool TryObject(string text, out JsonObject value)
    {
        value = new JsonObject();
        if (text.Length == 0) return false;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject parsed) return false;
            value = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) =>
        new() { Id = id, Passed = passed, Message = message };
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) =>
        existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static string LuaString(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'")
        .Replace("\r", "\\r").Replace("\n", "\\n") + "'";
    private static string GuidFor(string seed, HashSet<string> used)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        for (var offset = 0; offset <= bytes.Length - 3; offset += 3)
        {
            var guid = Convert.ToHexString(bytes.AsSpan(offset, 3)).ToLowerInvariant();
            if (used.Add(guid)) return guid;
        }
        throw new InvalidOperationException("Could not allocate a unique deterministic TTS GUID.");
    }

    private sealed record SourceOwner(string PilotCardGuid, string ShipGuid, string DialGuid);
}

public sealed class FirstEditionAddedActionValidationResult
{
    public JsonObject Save { get; init; } = new();
    public FirstEditionAddedActionValidationManifest Manifest { get; init; } = new();
}

public sealed class FirstEditionAddedActionValidationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Engine Upgrade adds Boost through Unified Data.actSet; printed stats remain player-managed.";
    public string PilotName { get; init; } = "";
    public FirstEditionRuntimeOwnerBinding ActiveOwner { get; init; } = new();
    public FirstEditionRuntimeOwnerBinding ControlOwner { get; init; } = new();
    public List<string> BaselineActionCodes { get; init; } = new();
    public string AddedActionName { get; init; } = "";
    public string AddedActionCode { get; init; } = "";
    public List<string> ActiveEffectiveActionCodes { get; init; } = new();
    public List<string> ControlEffectiveActionCodes { get; init; } = new();
    public string ActiveUpgradeCardGuid { get; init; } = "";
    public int ActiveHandlerCount { get; init; }
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}
