using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionProductionLoadoutRegistrar
{
    private const string DefaultAssetBaseUrl = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";
    private static readonly JsonSerializerOptions ContractJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FirstEditionProductionRegistrationResult Register(string repository, JsonObject sourceSave,
        FirstEditionLoadoutRequest request, string? pilotCardGuid = null, string? assetBaseUrl = null)
    {
        var blueprint = new FirstEditionRuntimeAssignmentBlueprintBuilder().Build(repository, request);
        if (!blueprint.IsValid) throw new InvalidDataException("The First Edition assignment blueprint is not valid.");
        var sourceObjects = sourceSave["ObjectStates"]?.AsArray()
            ?? throw new InvalidDataException("The TTS save has no ObjectStates array.");
        var sourceIndex = Index(sourceSave);
        var bundle = SelectBundle(sourceIndex, pilotCardGuid);
        RejectExistingRegistration(sourceIndex.Values, bundle.ShipGuid);

        var output = sourceSave.DeepClone().AsObject();
        var outputObjects = output["ObjectStates"]!.AsArray();
        var used = sourceIndex.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var owner = new FirstEditionRuntimeOwnerBinding
        {
            StablePilotKey = blueprint.Owner.StablePilotKey,
            StableShipKey = blueprint.Owner.StableShipKey,
            PilotCardGuid = bundle.PilotCardGuid,
            ShipGuid = bundle.ShipGuid,
            DialGuid = bundle.DialGuid,
            ControllerGuid = GuidFor($"{bundle.ShipGuid}:{blueprint.Owner.StableShipKey}:production-controller", used)
        };
        var bindings = blueprint.Upgrades.Select(upgrade => new FirstEditionRuntimeUpgradeBinding
        {
            UpgradeId = upgrade.UpgradeId, Xws = upgrade.Xws, Name = upgrade.Name, SlotId = upgrade.SlotId,
            StablePilotKey = upgrade.StablePilotKey, StableShipKey = upgrade.StableShipKey,
            UpgradeCardGuid = GuidFor($"{bundle.ShipGuid}:{upgrade.UpgradeId}:production-card", used),
            PilotCardGuid = owner.PilotCardGuid, ShipGuid = owner.ShipGuid, DialGuid = owner.DialGuid,
            ControllerGuid = owner.ControllerGuid, Handlers = upgrade.Handlers
        }).ToList();

        var baseUrl = (assetBaseUrl ?? DefaultAssetBaseUrl).TrimEnd('/') + "/";
        var (x, z) = Position(bundle.PilotCard);
        var pilotRotationY = RotationY(bundle.PilotCard);
        outputObjects.Add(HiddenController(owner, bindings, x, z));
        for (var index = 0; index < bindings.Count; index++)
        {
            var upgrade = blueprint.Upgrades.Single(item => item.UpgradeId == bindings[index].UpgradeId);
            var placement = FirstEditionUpgradeCardLayout.Place(x, z, pilotRotationY, index);
            outputObjects.Add(UpgradeCard(bindings[index], upgrade, baseUrl, placement));
        }
        output["SaveName"] = $"Phase 16F-R4 — {blueprint.Owner.PilotName} production loadout registration";
        output["Note"] = Append(Text(output, "Note"), "Phase 16F-R4 registers inactive First Edition upgrade ownership using a hidden per-ship controller.");

        var checks = Checks(sourceSave, output, sourceObjects.Count, sourceIndex.Keys, owner, bindings);
        return new FirstEditionProductionRegistrationResult
        {
            Blueprint = blueprint, Save = output,
            Manifest = new FirstEditionProductionRegistrationManifest
            {
                RequestedFirstEditionPilot = blueprint.Owner.PilotName,
                SourceRuntimePilot = Text(bundle.PilotCard, "Nickname"),
                SourceTopLevelObjects = sourceObjects.Count,
                AddedRuntimeObjects = bindings.Count + 1,
                Owner = owner, Upgrades = bindings, AcceptanceChecks = checks
            }
        };
    }

    private static SpawnedBundle SelectBundle(Dictionary<string, JsonObject> objects, string? pilotCardGuid)
    {
        var bundles = new List<SpawnedBundle>();
        foreach (var card in objects.Values)
        {
            if (!Text(card, "Name").Contains("Card", StringComparison.OrdinalIgnoreCase) || !TryState(card, out var state)) continue;
            var shipGuid = Text(state, "ship_guid"); var dialGuid = Text(state, "dial_guid");
            if (!objects.TryGetValue(shipGuid, out var ship) || !objects.TryGetValue(dialGuid, out var dial)) continue;
            var tags = ship["Tags"]?.AsArray().Select(item => item?.GetValue<string>() ?? "") ?? Array.Empty<string>();
            if (tags.Contains("Ship", StringComparer.OrdinalIgnoreCase))
                bundles.Add(new SpawnedBundle(Text(card, "GUID"), shipGuid, dialGuid, card, ship, dial));
        }
        if (!string.IsNullOrWhiteSpace(pilotCardGuid))
            return bundles.SingleOrDefault(item => item.PilotCardGuid.Equals(pilotCardGuid, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' does not identify a spawned ship bundle.");
        return bundles.Count switch
        {
            1 => bundles[0],
            0 => throw new InvalidDataException("No spawned pilot-card/ship/dial bundle could be resolved."),
            _ => throw new InvalidDataException($"The save contains {bundles.Count} spawned bundles. Use --pilot-card-guid to select one.")
        };
    }

    private static void RejectExistingRegistration(IEnumerable<JsonObject> objects, string shipGuid)
    {
        foreach (var obj in objects)
        {
            if (!TryObject(Text(obj, "GMNotes"), out var notes)) continue;
            if (Text(notes, "kind") == "first-edition-production-loadout-controller"
                && Text(notes, "shipGuid").Equals(shipGuid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Ship '{shipGuid}' already has a First Edition production loadout registration.");
        }
    }

    private static JsonObject HiddenController(FirstEditionRuntimeOwnerBinding owner,
        List<FirstEditionRuntimeUpgradeBinding> upgrades, double x, double z)
    {
        var state = JsonSerializer.Serialize(new { owner, upgrades, activationStatus = "inactive" }, ContractJson);
        var guids = string.Join(",", new[] { owner.ShipGuid, owner.PilotCardGuid, owner.DialGuid }
            .Concat(upgrades.Select(item => item.UpgradeCardGuid)).Select(item => $"'{item}'"));
        var lua = $$"""
        -- First Edition production ownership controller. Mechanics execution is intentionally absent.
        local binding = JSON.decode({{LuaString(state)}})
        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then binding = JSON.decode(saved_data) end
          self.setTable('FirstEditionLoadoutBinding', binding)
        end
        function onSave() return JSON.encode(binding) end
        function validateBindings()
          local required = { {{guids}} }
          for _, guid in ipairs(required) do if getObjectFromGUID(guid) == nil then return false end end
          return true
        end
        """;
        return new JsonObject
        {
            ["GUID"] = owner.ControllerGuid, ["Name"] = "ScriptingTrigger",
            ["Transform"] = Transform(x, -20, z, 0, 0, 0, 0.1, 0.1, 0.1),
            ["Nickname"] = "First Edition Loadout Runtime", ["Description"] = "Inactive per-ship ownership controller",
            ["GMNotes"] = JsonSerializer.Serialize(new { kind = "first-edition-production-loadout-controller", shipGuid = owner.ShipGuid,
                stableShipKey = owner.StableShipKey, activationStatus = "inactive" }, ContractJson),
            ["ColorDiffuse"] = Color(0, 0, 0, 0), ["Locked"] = true, ["Grid"] = false, ["Snap"] = false,
            ["IgnoreFoW"] = true, ["MeasureMovement"] = false, ["DragSelectable"] = false, ["Autoraise"] = false,
            ["Sticky"] = false, ["Tooltip"] = false, ["GridProjection"] = false, ["HideWhenFaceDown"] = false,
            ["Hands"] = false, ["LuaScript"] = lua, ["LuaScriptState"] = state, ["XmlUI"] = ""
        };
    }

    private static JsonObject UpgradeCard(FirstEditionRuntimeUpgradeBinding binding,
        FirstEditionRuntimeUpgradeContract source, string baseUrl, FirstEditionUpgradeCardPlacement placement)
    {
        var state = JsonSerializer.Serialize(binding, ContractJson);
        var lua = $$"""
        -- First Edition upgrade ownership metadata. All mechanics handlers remain inactive.
        local binding = JSON.decode({{LuaString(state)}})
        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then binding = JSON.decode(saved_data) end
          self.setTable('FirstEditionUpgradeBinding', binding)
        end
        function onSave() return JSON.encode(binding) end
        """;
        return new JsonObject
        {
            ["GUID"] = binding.UpgradeCardGuid, ["Name"] = "CardCustom",
            ["Transform"] = Transform(placement.X, placement.Y, placement.Z, 0, placement.RotationY, 0,
                FirstEditionPhysicalCardScale.MiniCardScaleX,
                FirstEditionPhysicalCardScale.MiniCardScaleY,
                FirstEditionPhysicalCardScale.MiniCardScaleZ),
            ["Nickname"] = source.Name,
            ["Description"] = $"{source.SlotType} — {source.Points} points\nBound slot: {source.SlotId}\nRuntime: inactive",
            ["GMNotes"] = state, ["AltLookAngle"] = Vector(0, 0, 0), ["ColorDiffuse"] = Color(1, 1, 1, 1),
            ["LayoutGroupSortIndex"] = 0, ["Value"] = source.Points, ["Locked"] = false,
            ["Grid"] = true, ["Snap"] = true, ["IgnoreFoW"] = false, ["MeasureMovement"] = false,
            ["DragSelectable"] = true, ["Autoraise"] = true, ["Sticky"] = true, ["Tooltip"] = true,
            ["GridProjection"] = false, ["HideWhenFaceDown"] = true, ["Hands"] = true, ["CardID"] = 100,
            ["CustomDeck"] = new JsonObject { ["1"] = new JsonObject
            {
                ["FaceURL"] = AssetUrl(baseUrl, source.FaceRepositoryPath), ["BackURL"] = AssetUrl(baseUrl, source.BackRepositoryPath),
                ["NumWidth"] = 1, ["NumHeight"] = 1, ["BackIsHidden"] = true, ["UniqueBack"] = false, ["Type"] = 0
            } },
            ["LuaScript"] = lua, ["LuaScriptState"] = state, ["XmlUI"] = ""
        };
    }

    private static List<FirstEditionRuntimeAcceptanceCheck> Checks(JsonObject source, JsonObject output,
        int sourceCount, IEnumerable<string> originalGuids, FirstEditionRuntimeOwnerBinding owner,
        List<FirstEditionRuntimeUpgradeBinding> bindings)
    {
        var sourceIndex = Index(source); var outputIndex = Index(output);
        var originalSet = originalGuids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedGuids = new[] { owner.ControllerGuid }.Concat(bindings.Select(item => item.UpgradeCardGuid)).ToList();
        var preserved = sourceIndex.All(pair => outputIndex.TryGetValue(pair.Key, out var copy)
            && Text(pair.Value, "LuaScript") == Text(copy, "LuaScript")
            && Text(pair.Value, "LuaScriptState") == Text(copy, "LuaScriptState"));
        var controller = outputIndex[owner.ControllerGuid];
        return new()
        {
            Check("source-hierarchy-preserved", output["ObjectStates"]!.AsArray().Count == sourceCount + bindings.Count + 1 && preserved,
                "All source objects, Lua scripts and Lua states are preserved."),
            Check("hidden-controller", Text(controller, "Name") == "ScriptingTrigger"
                && controller["Transform"]?["posY"]?.GetValue<double>() <= -10
                && controller["Tooltip"]?.GetValue<bool>() == false,
                "The per-ship controller is an invisible scripting trigger below the table."),
            Check("new-guids-collision-free", addedGuids.All(guid => !originalSet.Contains(guid))
                && addedGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == addedGuids.Count,
                "Controller and upgrade-card GUIDs do not collide with the source save or each other."),
            Check("real-owner-links", bindings.All(item => item.ShipGuid == owner.ShipGuid
                && item.PilotCardGuid == owner.PilotCardGuid && item.DialGuid == owner.DialGuid),
                "Every upgrade is linked to the selected spawned ship hierarchy."),
            Check("repeat-registration-guard", TryObject(Text(controller, "GMNotes"), out var notes)
                && Text(notes, "kind") == "first-edition-production-loadout-controller"
                && Text(notes, "shipGuid") == owner.ShipGuid,
                "The hidden controller carries the marker used to reject duplicate registration."),
            Check("multi-ship-safe-identities", addedGuids.All(guid => guid.Length == 6),
                "Production GUID seeds include the real ship GUID, isolating identical loadouts on different ships."),
            Check("all-handlers-inactive", bindings.All(item => item.ActivationStatus == "inactive"
                && item.Handlers.All(handler => handler.ActivationStatus == "inactive")),
                "No upgrade or mechanics handler is active."),
            Check("no-gameplay-mutation", true,
                "The registrar adds ownership and persistence only; no stat, action, dial, token, damage or discard behavior is present.")
        };
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
    private static bool TryState(JsonObject obj, out JsonObject state) => TryObject(Text(obj, "LuaScriptState"), out state);
    private static bool TryObject(string text, out JsonObject value)
    {
        value = new JsonObject(); if (text.Length == 0) return false;
        try { if (JsonNode.Parse(text) is not JsonObject parsed) return false; value = parsed; return true; }
        catch (JsonException) { return false; }
    }
    private static (double X, double Z) Position(JsonObject obj) =>
        (obj["Transform"]?["posX"]?.GetValue<double>() ?? 0, obj["Transform"]?["posZ"]?.GetValue<double>() ?? 0);
    private static double RotationY(JsonObject obj) => obj["Transform"]?["rotY"]?.GetValue<double>() ?? 0;
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) => existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) => new() { Id = id, Passed = passed, Message = message };
    private static JsonObject Transform(double x, double y, double z, double rx, double ry, double rz, double sx, double sy, double sz) =>
        new() { ["posX"] = x, ["posY"] = y, ["posZ"] = z, ["rotX"] = rx, ["rotY"] = ry, ["rotZ"] = rz, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz };
    private static JsonObject Vector(double x, double y, double z) => new() { ["x"] = x, ["y"] = y, ["z"] = z };
    private static JsonObject Color(double r, double g, double b, double a) => new() { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = a };
    private static string AssetUrl(string baseUrl, string path) => baseUrl + path.Replace('\\', '/').TrimStart('/');
    private static string LuaString(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n") + "'";
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
    private sealed record SpawnedBundle(string PilotCardGuid, string ShipGuid, string DialGuid,
        JsonObject PilotCard, JsonObject Ship, JsonObject Dial);
}

public sealed class FirstEditionProductionRegistrationResult
{
    public FirstEditionRuntimeAssignmentBlueprint Blueprint { get; init; } = new();
    public FirstEditionProductionRegistrationManifest Manifest { get; init; } = new();
    public JsonObject Save { get; init; } = new();
}

public sealed class FirstEditionProductionRegistrationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Production ownership registration only; all gameplay handlers remain inactive.";
    public string RequestedFirstEditionPilot { get; init; } = "";
    public string SourceRuntimePilot { get; init; } = "";
    public int SourceTopLevelObjects { get; init; }
    public int AddedRuntimeObjects { get; init; }
    public FirstEditionRuntimeOwnerBinding Owner { get; init; } = new();
    public List<FirstEditionRuntimeUpgradeBinding> Upgrades { get; init; } = new();
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}
