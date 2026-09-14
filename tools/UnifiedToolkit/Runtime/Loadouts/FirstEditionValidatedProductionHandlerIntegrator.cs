using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionValidatedProductionHandlerIntegrator
{
    private static readonly JsonSerializerOptions ContractJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    private static readonly IReadOnlyDictionary<string, string> ActionCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["focus"] = "F", ["targetlock"] = "TL", ["target lock"] = "TL",
            ["evade"] = "E", ["reinforce"] = "R", ["calculate"] = "C",
            ["cloak"] = "CL", ["barrelroll"] = "BR", ["barrel roll"] = "BR", ["boost"] = "B"
        };

    public FirstEditionProductionRegistrationResult Apply(string repository,
        FirstEditionLoadoutRequest request, FirstEditionProductionRegistrationResult registration,
        string? assetBaseUrl = null)
    {
        var output = registration.Save;
        var index = Index(output);
        var owner = registration.Manifest.Owner;
        var ship = index[owner.ShipGuid];
        var controller = index[owner.ControllerGuid];
        var baseline = new FirstEditionLoadoutPlanner().Plan(repository, new FirstEditionLoadoutRequest
        {
            Pilot = request.Pilot, Ship = request.Ship, Faction = request.Faction
        });
        if (!baseline.IsValid) throw new InvalidDataException("The First Edition production baseline is not valid.");

        var xws = registration.Blueprint.Upgrades.Select(item => Key(item.Xws)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engineActive = xws.Contains("engineupgrade");
        var r2Active = xws.Contains("r2astromech");
        var r2d6Active = xws.Contains("r2d6");
        var shieldActive = xws.Contains("shieldupgrade");
        var baselineActions = baseline.Ship.Actions.Select(ToActionCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var effectiveActions = baselineActions
            .Concat(engineActive ? new[] { "B" } : System.Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PersistActions(ship, effectiveActions);

        var baselineMoves = PersistedMoves(ship);
        var effectiveMoves = r2Active
            ? FirstEditionManeuverDifficultyHandler.TreatSpeedsAsEasy(baselineMoves, 1, 2)
            : baselineMoves.ToList();
        if (r2Active) PersistMoves(ship, effectiveMoves);

        FirstEditionShieldTokenResult? shieldToken = null;
        if (shieldActive)
            shieldToken = new FirstEditionShieldUpgradeHandler().Apply(
                output, owner, registration.Blueprint.Owner.PilotName, assetBaseUrl);

        var activeBindings = registration.Manifest.Upgrades.Select(binding => ActivateBinding(binding)).ToList();
        foreach (var binding in activeBindings)
            ActivateCard(index[binding.UpgradeCardGuid], binding);

        var state = new JsonObject
        {
            ["owner"] = JsonSerializer.SerializeToNode(owner, ContractJson),
            ["activationStatus"] = "validated-production-handlers",
            ["effectiveActionCodes"] = Array(effectiveActions),
            ["effectiveMoveSet"] = Array(effectiveMoves),
            ["applyMoveSet"] = r2Active,
            ["shieldTokenGuid"] = shieldToken?.TokenGuid ?? "",
            ["structuralSlots"] = JsonSerializer.SerializeToNode(registration.Blueprint.Slots, ContractJson),
            ["upgrades"] = JsonSerializer.SerializeToNode(activeBindings, ContractJson),
            ["applied"] = false
        };
        var serialized = state.ToJsonString();
        controller["Description"] = "First Edition production runtime — validated handlers only";
        controller["LuaScriptState"] = serialized;
        controller["LuaScript"] = ControllerLua(serialized);
        if (TryObject(Text(controller, "GMNotes"), out var controllerNotes))
        {
            controllerNotes["activationStatus"] = "validated-production-handlers";
            controllerNotes["activeHandlerCount"] = activeBindings.Sum(item =>
                item.Handlers.Count(handler => handler.ActivationStatus != "inactive"));
            controllerNotes["shieldTokenGuid"] = shieldToken?.TokenGuid ?? "";
            controller["GMNotes"] = controllerNotes.ToJsonString();
        }

        var activations = activeBindings.SelectMany(binding => binding.Handlers
            .Where(handler => handler.ActivationStatus != "inactive")
            .Select(handler => new FirstEditionProductionHandlerActivation
            {
                UpgradeXws = binding.Xws,
                UpgradeCardGuid = binding.UpgradeCardGuid,
                MechanicId = handler.MechanicId,
                HandlerId = handler.HandlerId,
                Status = handler.ActivationStatus
            })).ToList();
        var oldChecks = registration.Manifest.AcceptanceChecks
            .Where(check => check.Id != "source-hierarchy-preserved"
                && check.Id != "all-handlers-inactive" && check.Id != "no-gameplay-mutation")
            .ToList();
        oldChecks.Add(Check("validated-handler-allowlist",
            activations.All(item => Supports(item.UpgradeXws, item.MechanicId)),
            "Every active handler is on the reviewed card-and-mechanic allowlist."));
        oldChecks.Add(Check("first-edition-action-state", PersistedActions(ship)
                .SequenceEqual(effectiveActions, StringComparer.OrdinalIgnoreCase),
            "The ship's saved action state is the First Edition baseline plus validated additions."));
        oldChecks.Add(Check("maneuver-state", !r2Active || PersistedMoves(ship)
                .SequenceEqual(effectiveMoves, StringComparer.OrdinalIgnoreCase),
            "R2 Astromech's effective manoeuvre set is persisted before dial initialization."));
        oldChecks.Add(Check("structural-slot-state", !r2d6Active
                || registration.Blueprint.Slots.Any(slot => slot.Source == "upgrade:r2d6" && slot.Type == "Elite"),
            "R2-D6's generated Elite slot is present before dependent upgrade assignment."));
        oldChecks.Add(Check("physical-shield-state", !shieldActive || shieldToken is not null,
            "Shield Upgrade creates one explicitly owned physical shield token."));
        oldChecks.Add(Check("controller-composes-active-bridges",
            Text(controller, "LuaScript").Contains("applyFirstEditionProductionState", StringComparison.Ordinal),
            "One hidden controller composes action and manoeuvre bridges without replacing Unified runtime objects."));

        output["SaveName"] = $"Phase 16F-R11 — {registration.Blueprint.Owner.PilotName} validated production handlers";
        output["Note"] = Append(Text(output, "Note"),
            "Phase 16F-R11 activates only reviewed Engine Upgrade, R2 Astromech, R2-D6 and Shield Upgrade handlers. Every other mechanic remains inactive.");
        return new FirstEditionProductionRegistrationResult
        {
            Blueprint = registration.Blueprint,
            Save = output,
            Manifest = new FirstEditionProductionRegistrationManifest
            {
                Policy = "Production registration with the reviewed four-card handler allowlist.",
                RequestedFirstEditionPilot = registration.Manifest.RequestedFirstEditionPilot,
                SourceRuntimePilot = registration.Manifest.SourceRuntimePilot,
                SourceTopLevelObjects = registration.Manifest.SourceTopLevelObjects,
                AddedRuntimeObjects = registration.Manifest.AddedRuntimeObjects + (shieldToken is null ? 0 : 1),
                Owner = owner,
                Upgrades = activeBindings,
                ValidatedHandlersEnabled = true,
                HandlerActivations = activations,
                SpawnedResourceTokenGuids = shieldToken is null ? new() : new() { shieldToken.TokenGuid },
                AcceptanceChecks = oldChecks
            }
        };
    }

    private static FirstEditionRuntimeUpgradeBinding ActivateBinding(FirstEditionRuntimeUpgradeBinding binding)
    {
        var handlers = binding.Handlers.Select(handler => new FirstEditionRuntimeHandlerContract
        {
            HandlerId = handler.HandlerId, MechanicId = handler.MechanicId, Name = handler.Name,
            ReviewStatus = handler.ReviewStatus, CatalogueRuntimeStatus = handler.CatalogueRuntimeStatus,
            Evidence = handler.Evidence.ToList(),
            ActivationStatus = Supports(binding.Xws, handler.MechanicId) ? ActiveStatus(binding.Xws) : "inactive"
        }).ToList();
        return new FirstEditionRuntimeUpgradeBinding
        {
            UpgradeId = binding.UpgradeId, Xws = binding.Xws, Name = binding.Name, SlotId = binding.SlotId,
            StablePilotKey = binding.StablePilotKey, StableShipKey = binding.StableShipKey,
            UpgradeCardGuid = binding.UpgradeCardGuid, PilotCardGuid = binding.PilotCardGuid,
            ShipGuid = binding.ShipGuid, DialGuid = binding.DialGuid, ControllerGuid = binding.ControllerGuid,
            BindingStatus = binding.BindingStatus,
            ActivationStatus = handlers.Any(handler => handler.ActivationStatus != "inactive") ? "active" : "inactive",
            Handlers = handlers
        };
    }

    private static void ActivateCard(JsonObject card, FirstEditionRuntimeUpgradeBinding binding)
    {
        var state = JsonSerializer.Serialize(binding, ContractJson);
        card["GMNotes"] = state;
        card["LuaScriptState"] = state;
        card["Description"] = Text(card, "Description").Replace("Runtime: inactive",
            binding.ActivationStatus == "active" ? "Runtime: active — validated handler" : "Runtime: inactive");
    }

    private static bool Supports(string xws, string mechanicId) => (Key(xws), mechanicId) switch
    {
        ("engineupgrade", "adds-action") => true,
        ("r2astromech", "maneuver-difficulty-change") => true,
        ("r2d6", "upgrade-slot-change") => true,
        ("shieldupgrade", "stat-change") => true,
        _ => false
    };
    private static string ActiveStatus(string xws) => Key(xws) switch
    {
        "engineupgrade" => "active-added-action",
        "r2astromech" => "active-maneuver-colour",
        "r2d6" => "active-structural-slot",
        "shieldupgrade" => "active-physical-shield",
        _ => "inactive"
    };

    private static string ControllerLua(string state) => $$"""
        -- First Edition production runtime. Only explicitly validated handlers are represented in config.
        local config = JSON.decode({{LuaString(state)}})
        local attempts = 0
        local function copy(values)
          local result = {}
          for _, value in ipairs(values or {}) do table.insert(result, value) end
          return result
        end
        function applyFirstEditionProductionState()
          local ship = getObjectFromGUID(config.owner.shipGuid)
          if ship == nil then return false end
          local data = ship.getTable('Data')
          if data == nil then return false end
          data.actSet = copy(config.effectiveActionCodes)
          ship.setTable('Data', data)
          if config.applyMoveSet then ship.call('setMoveSet', { moveSet = copy(config.effectiveMoveSet) }) end
          config.applied = true
          self.setTable('FirstEditionLoadoutBinding', config)
          return true
        end
        function applyWhenReady()
          attempts = attempts + 1
          if applyFirstEditionProductionState() then return end
          if attempts < 180 then Wait.frames(applyWhenReady, 1) end
        end
        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then config = JSON.decode(saved_data) end
          config.applied = false
          self.setTable('FirstEditionLoadoutBinding', config)
          Wait.frames(applyWhenReady, 5)
        end
        function onSave() return JSON.encode(config) end
        function validateBindings()
          return getObjectFromGUID(config.owner.shipGuid) ~= nil
            and getObjectFromGUID(config.owner.pilotCardGuid) ~= nil
            and getObjectFromGUID(config.owner.dialGuid) ~= nil
        end
        """;

    private static void PersistActions(JsonObject ship, IReadOnlyList<string> values)
    {
        PersistShipValues(ship, "actSet", values);
    }
    private static void PersistMoves(JsonObject ship, IReadOnlyList<string> values)
    {
        PersistShipValues(ship, "moveSet", values);
    }
    private static List<string> PersistedActions(JsonObject ship) => Values(ShipState(ship)["actSet"] as JsonArray);
    private static List<string> PersistedMoves(JsonObject ship) => Values(ShipState(ship)["moveSet"] as JsonArray);
    private static JsonObject ShipState(JsonObject ship)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state) || state["shipData"] is not JsonObject data)
            throw new InvalidDataException($"Ship GUID '{Text(ship, "GUID")}' has no persisted shipData state.");
        return data;
    }
    private static void PersistShipValues(JsonObject ship, string key, IReadOnlyList<string> values)
    {
        if (!TryObject(Text(ship, "LuaScriptState"), out var state)
            || state["shipData"] is not JsonObject data)
            throw new InvalidDataException($"Ship GUID '{Text(ship, "GUID")}' has no persisted shipData state.");
        data[key] = Array(values);
        ship["LuaScriptState"] = state.ToJsonString();
    }
    private static List<string> Values(JsonArray? values) => values?.Select(item => item?.GetValue<string>() ?? "")
        .Where(value => value.Length > 0).ToList() ?? new();
    private static string ToActionCode(string action)
    {
        var key = Key(action); if (ActionCodes.TryGetValue(key, out var code)) return code;
        throw new InvalidDataException($"First Edition action '{action}' has no Unified runtime action-code mapping.");
    }
    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(value => JsonValue.Create(value)).ToArray());
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
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) => existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static string Key(string? value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string LuaString(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n") + "'";
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) => new() { Id = id, Passed = passed, Message = message };
}

public sealed class FirstEditionProductionHandlerActivation
{
    public string UpgradeXws { get; init; } = "";
    public string UpgradeCardGuid { get; init; } = "";
    public string MechanicId { get; init; } = "";
    public string HandlerId { get; init; } = "";
    public string Status { get; init; } = "";
}
