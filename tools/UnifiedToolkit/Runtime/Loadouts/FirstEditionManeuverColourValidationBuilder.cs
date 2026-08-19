using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionManeuverColourValidationBuilder
{
    private const string R2AstromechXws = "r2astromech";
    private static readonly JsonSerializerOptions ContractJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public FirstEditionManeuverColourValidationResult Build(string repository, string sourceSavePath,
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

        var activeBaseline = PersistedMoveSet(sourceIndex[activeSourceOwner.ShipGuid]);
        var controlBaseline = PersistedMoveSet(sourceIndex[controlSourceOwner.ShipGuid]);
        if (activeBaseline.Count == 0 || !activeBaseline.SequenceEqual(controlBaseline, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The active and control ships must begin with the same non-empty manoeuvre set.");
        var effectiveMoveSet = FirstEditionManeuverDifficultyHandler.TreatSpeedsAsEasy(activeBaseline, 1, 2);
        var changedMoves = activeBaseline.Zip(effectiveMoveSet)
            .Where(pair => !pair.First.Equals(pair.Second, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new FirstEditionManeuverChange
            {
                Original = pair.First,
                Effective = pair.Second,
                Speed = FirstEditionManeuverDifficultyHandler.Speed(pair.First)
            }).ToList();

        var request = new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot,
            Ship = pilotRequest.Ship,
            Faction = pilotRequest.Faction,
            Upgrades = new() { R2AstromechXws }
        };
        var registration = new FirstEditionProductionLoadoutRegistrar().Register(
            repository, source, request, activePilotCardGuid, assetBaseUrl);
        var upgrade = registration.Manifest.Upgrades.SingleOrDefault(item =>
            item.Xws.Equals(R2AstromechXws, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("R2 Astromech was not registered.");
        var contract = registration.Blueprint.Upgrades.Single(item =>
            item.Xws.Equals(R2AstromechXws, StringComparison.OrdinalIgnoreCase));
        if (!contract.Handlers.Any(handler => handler.MechanicId == "maneuver-difficulty-change"))
            throw new InvalidDataException("R2 Astromech has no manoeuvre-difficulty handler contract.");

        var output = registration.Save;
        var outputObjects = output["ObjectStates"]!.AsArray();
        var outputIndex = Index(output);
        var activeController = outputIndex[registration.Manifest.Owner.ControllerGuid];
        var activeCard = outputIndex[upgrade.UpgradeCardGuid];
        var usedGuids = outputIndex.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var controlControllerGuid = GuidFor($"{controlSourceOwner.ShipGuid}:first-edition-move-baseline-controller", usedGuids);

        PersistMoveSet(outputIndex[activeSourceOwner.ShipGuid], effectiveMoveSet);
        PersistMoveSet(outputIndex[controlSourceOwner.ShipGuid], controlBaseline);
        var activeState = ControllerState(registration.Manifest.Owner, activeBaseline, effectiveMoveSet, true,
            upgrade.UpgradeCardGuid);
        ActivateController(activeController, activeState, "R2 Astromech — active manoeuvre-colour controller");
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
        var controlController = activeController.DeepClone().AsObject();
        controlController["GUID"] = controlControllerGuid;
        ActivateController(controlController,
            ControllerState(controlOwner, controlBaseline, controlBaseline, false, ""),
            "First Edition manoeuvre baseline — control ship");
        controlController["GMNotes"] = JsonSerializer.Serialize(new
        {
            kind = "first-edition-maneuver-baseline-controller",
            shipGuid = controlOwner.ShipGuid,
            activationStatus = "baseline-only"
        }, ContractJson);
        outputObjects.Add(controlController);

        output["SaveName"] = "Phase 16F-R8 — R2 Astromech manoeuvre-colour validation";
        output["Note"] = Append(Text(output, "Note"),
            "Phase 16F-R8 treats every speed-1 and speed-2 manoeuvre as First Edition green on the R2 Astromech ship only. Unified runtime code b represents the easy/green difficulty.");

        var outputIndexAfter = Index(output);
        var activePersisted = PersistedMoveSet(outputIndexAfter[activeSourceOwner.ShipGuid]);
        var controlPersisted = PersistedMoveSet(outputIndexAfter[controlSourceOwner.ShipGuid]);
        var targetShipGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            activeSourceOwner.ShipGuid,
            controlSourceOwner.ShipGuid
        };
        var sourceScriptsPreserved = sourceIndex.All(pair => outputIndexAfter.TryGetValue(pair.Key, out var copy)
            && Text(pair.Value, "LuaScript") == Text(copy, "LuaScript"));
        var nonTargetStatesPreserved = sourceIndex.Where(pair => !targetShipGuids.Contains(pair.Key))
            .All(pair => outputIndexAfter.TryGetValue(pair.Key, out var copy)
                && Text(pair.Value, "LuaScriptState") == Text(copy, "LuaScriptState"));
        var activeLua = Text(activeController, "LuaScript");
        var checks = new List<FirstEditionRuntimeAcceptanceCheck>
        {
            Check("matching-baselines", activeBaseline.SequenceEqual(controlBaseline, StringComparer.OrdinalIgnoreCase),
                "The active and control ships start with identical manoeuvre sets."),
            Check("speed-one-two-easy", effectiveMoveSet.All(move =>
                    FirstEditionManeuverDifficultyHandler.Speed(move) is not (1 or 2) || move[0] == 'b'),
                "Every speed-1 and speed-2 manoeuvre uses Unified easy code b, representing First Edition green."),
            Check("other-speeds-unchanged", activeBaseline.Zip(effectiveMoveSet).All(pair =>
                    FirstEditionManeuverDifficultyHandler.Speed(pair.First) is 1 or 2 || pair.First == pair.Second),
                "R2 Astromech does not alter manoeuvres at other speeds."),
            Check("maneuver-shapes-preserved", activeBaseline.Zip(effectiveMoveSet).All(pair =>
                    pair.First.Length == pair.Second.Length && pair.First[1..] == pair.Second[1..]),
                "Only the difficulty prefix changes; manoeuvre shape and speed remain intact."),
            Check("observable-change", changedMoves.Count > 0,
                "The chosen donor dial contains at least one speed-1 or speed-2 manoeuvre whose difficulty changes."),
            Check("persisted-move-state", activePersisted.SequenceEqual(effectiveMoveSet, StringComparer.OrdinalIgnoreCase)
                && controlPersisted.SequenceEqual(controlBaseline, StringComparer.OrdinalIgnoreCase),
                "Both saved ship states are prepared before the normal Unified dial load lifecycle."),
            Check("existing-unified-runtime-reused", activeLua.Contains("ship.call('setMoveSet'", StringComparison.Ordinal)
                && !activeLua.Contains("dial.call('assignShip'", StringComparison.Ordinal),
                "The controller reuses the ship setMoveSet API without racing the dial's own initialization."),
            Check("owner-isolation", registration.Manifest.Owner.ShipGuid != controlOwner.ShipGuid
                && registration.Manifest.Owner.ControllerGuid != controlOwner.ControllerGuid,
                "Active and control ships use distinct owner hierarchies and hidden controllers."),
            Check("source-hierarchy-preserved", sourceScriptsPreserved && nonTargetStatesPreserved
                && outputObjects.Count == source["ObjectStates"]!.AsArray().Count + 3,
                "Source scripts and non-target states are preserved; only two ship move sets and three runtime objects differ."),
            Check("single-active-upgrade-handler", contract.Handlers.Count(handler =>
                    handler.MechanicId == "maneuver-difficulty-change") == 1,
                "R2 Astromech's manoeuvre-difficulty handler is the only active upgrade mechanic."),
            Check("source-input-unmodified", source.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString(),
                "The input save object was not modified in memory.")
        };

        return new FirstEditionManeuverColourValidationResult
        {
            Save = output,
            Manifest = new FirstEditionManeuverColourValidationManifest
            {
                PilotName = registration.Manifest.RequestedFirstEditionPilot,
                ActiveOwner = registration.Manifest.Owner,
                ControlOwner = controlOwner,
                BaselineMoveSet = activeBaseline,
                ActiveEffectiveMoveSet = effectiveMoveSet,
                ControlEffectiveMoveSet = controlBaseline,
                Changes = changedMoves,
                ActiveUpgradeCardGuid = upgrade.UpgradeCardGuid,
                ActiveHandlerCount = 1,
                AcceptanceChecks = checks
            }
        };
    }

    private static JsonObject ControllerState(FirstEditionRuntimeOwnerBinding owner, List<string> baseline,
        List<string> effective, bool active, string cardGuid) => new()
    {
        ["owner"] = JsonSerializer.SerializeToNode(owner, ContractJson),
        ["activationStatus"] = active ? "active" : "baseline-only",
        ["handlerId"] = active ? "maneuver-difficulty-change:r2astromech" : "first-edition-maneuver-baseline",
        ["mechanicId"] = active ? "maneuver-difficulty-change" : "baseline-move-set",
        ["upgradeXws"] = active ? R2AstromechXws : "",
        ["upgradeCardGuid"] = cardGuid,
        ["baselineMoveSet"] = Array(baseline),
        ["effectiveMoveSet"] = Array(effective),
        ["applied"] = false
    };

    private static void ActivateController(JsonObject controller, JsonObject state, string description)
    {
        var serialized = state.ToJsonString();
        controller["Description"] = description;
        controller["LuaScriptState"] = serialized;
        controller["LuaScript"] = ControllerLua(serialized);
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
            throw new InvalidDataException("R2 Astromech card metadata is not readable.");
        state["activationStatus"] = "active";
        if (state["handlers"] is JsonArray handlers)
            foreach (var item in handlers.OfType<JsonObject>())
                if (Text(item, "mechanicId") == "maneuver-difficulty-change") item["activationStatus"] = "active";
        var serialized = state.ToJsonString();
        card["GMNotes"] = serialized;
        card["LuaScriptState"] = serialized;
        card["Description"] = "Astromech — 1 point\nBound slot: astromech:1\nRuntime: active — speed 1–2 manoeuvres are green";
    }

    private static string ControllerLua(string state) => $$"""
        -- First Edition manoeuvre-colour bridge. Only the reviewed R2 Astromech handler is active.
        local config = JSON.decode({{LuaString(state)}})
        local attempts = 0

        local function copy(values)
          local result = {}
          for _, value in ipairs(values or {}) do table.insert(result, value) end
          return result
        end

        function applyFirstEditionMoveSet()
          local ship = getObjectFromGUID(config.owner.shipGuid)
          if ship == nil then return false end
          ship.call('setMoveSet', { moveSet = copy(config.effectiveMoveSet) })
          config.applied = true
          self.setTable('FirstEditionManeuverBinding', config)
          return true
        end

        function applyWhenReady()
          attempts = attempts + 1
          if applyFirstEditionMoveSet() then return end
          if attempts < 180 then Wait.frames(applyWhenReady, 1) end
        end

        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then config = JSON.decode(saved_data) end
          config.applied = false
          self.setTable('FirstEditionManeuverBinding', config)
          Wait.frames(applyWhenReady, 5)
        end

        function onSave() return JSON.encode(config) end
        """;

    private static void PersistMoveSet(JsonObject ship, IReadOnlyList<string> moves)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state) || state["shipData"] is not JsonObject data)
            throw new InvalidDataException($"Ship GUID '{Text(ship, "GUID")}' has no persisted shipData state.");
        data["moveSet"] = Array(moves);
        ship["LuaScriptState"] = state.ToJsonString();
    }

    private static List<string> PersistedMoveSet(JsonObject ship)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state)
            || state["shipData"] is not JsonObject data || data["moveSet"] is not JsonArray moves) return new();
        return moves.Select(item => item?.GetValue<string>() ?? "").Where(value => value.Length > 0).ToList();
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

    private static JsonArray Array(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray());
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

public sealed class FirstEditionManeuverColourValidationResult
{
    public JsonObject Save { get; init; } = new();
    public FirstEditionManeuverColourValidationManifest Manifest { get; init; } = new();
}

public sealed class FirstEditionManeuverColourValidationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "R2 Astromech changes effective moveSet difficulty only; printed dial artwork remains unchanged.";
    public string PilotName { get; init; } = "";
    public FirstEditionRuntimeOwnerBinding ActiveOwner { get; init; } = new();
    public FirstEditionRuntimeOwnerBinding ControlOwner { get; init; } = new();
    public List<string> BaselineMoveSet { get; init; } = new();
    public List<string> ActiveEffectiveMoveSet { get; init; } = new();
    public List<string> ControlEffectiveMoveSet { get; init; } = new();
    public List<FirstEditionManeuverChange> Changes { get; init; } = new();
    public string ActiveUpgradeCardGuid { get; init; } = "";
    public int ActiveHandlerCount { get; init; }
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}

public sealed class FirstEditionManeuverChange
{
    public string Original { get; init; } = "";
    public string Effective { get; init; } = "";
    public int Speed { get; init; }
}
