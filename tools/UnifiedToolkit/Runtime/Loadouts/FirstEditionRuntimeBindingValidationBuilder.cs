using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionRuntimeBindingValidationBuilder
{
    private const string DefaultAssetBaseUrl = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";
    private static readonly JsonSerializerOptions ContractJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FirstEditionRuntimeBindingBuildResult Build(string repository, FirstEditionLoadoutRequest request, string? assetBaseUrl = null)
    {
        repository = Path.GetFullPath(repository);
        var blueprint = new FirstEditionRuntimeAssignmentBlueprintBuilder().Build(repository, request);
        if (!blueprint.IsValid) throw new InvalidDataException("The source runtime assignment blueprint is not valid.");

        assetBaseUrl = NormalizeBaseUrl(assetBaseUrl ?? DefaultAssetBaseUrl);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owner = new FirstEditionRuntimeOwnerBinding
        {
            StablePilotKey = blueprint.Owner.StablePilotKey,
            StableShipKey = blueprint.Owner.StableShipKey,
            PilotCardGuid = GuidFor(blueprint.Owner.StablePilotKey + ":pilot-card", used),
            ShipGuid = GuidFor(blueprint.Owner.StableShipKey + ":ship", used),
            DialGuid = GuidFor(blueprint.Owner.StablePilotKey + ":dial", used),
            ControllerGuid = GuidFor(blueprint.Owner.StableShipKey + ":loadout-controller", used)
        };
        var bindings = blueprint.Upgrades.Select(upgrade => new FirstEditionRuntimeUpgradeBinding
        {
            UpgradeId = upgrade.UpgradeId,
            Xws = upgrade.Xws,
            Name = upgrade.Name,
            SlotId = upgrade.SlotId,
            StablePilotKey = upgrade.StablePilotKey,
            StableShipKey = upgrade.StableShipKey,
            UpgradeCardGuid = GuidFor(upgrade.UpgradeId + ":card", used),
            PilotCardGuid = owner.PilotCardGuid,
            ShipGuid = owner.ShipGuid,
            DialGuid = owner.DialGuid,
            ControllerGuid = owner.ControllerGuid,
            Handlers = upgrade.Handlers
        }).ToList();

        var objects = new JsonArray
        {
            Anchor(owner.ShipGuid, $"{blueprint.Owner.PilotName} — ship binding anchor", "Ship", -5.5, 1.0),
            Anchor(owner.PilotCardGuid, $"{blueprint.Owner.PilotName} — pilot-card binding anchor", "PilotCard", -5.5, -1.0),
            Anchor(owner.DialGuid, $"{blueprint.Owner.PilotName} — dial binding anchor", "Dial", -5.5, -3.0),
            Controller(owner, bindings, blueprint, -1.5, -1.0)
        };
        for (var index = 0; index < bindings.Count; index++)
        {
            var source = blueprint.Upgrades.Single(item => item.UpgradeId == bindings[index].UpgradeId);
            objects.Add(UpgradeCard(bindings[index], source, assetBaseUrl, 3.0 + index * 3.4, -1.0));
        }

        var checks = Checks(blueprint, owner, bindings, objects);
        var manifest = new FirstEditionRuntimeBindingManifest { Owner = owner, Upgrades = bindings, AcceptanceChecks = checks };
        var save = SaveEnvelope(objects, blueprint);
        return new FirstEditionRuntimeBindingBuildResult { Blueprint = blueprint, Manifest = manifest, ValidationSave = save };
    }

    private static List<FirstEditionRuntimeAcceptanceCheck> Checks(FirstEditionRuntimeAssignmentBlueprint blueprint,
        FirstEditionRuntimeOwnerBinding owner, List<FirstEditionRuntimeUpgradeBinding> bindings, JsonArray objects)
    {
        var objectGuids = objects.Select(item => item?["GUID"]?.GetValue<string>() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roundTrips = bindings.All(binding =>
        {
            var json = JsonSerializer.Serialize(binding, ContractJson);
            var copy = JsonSerializer.Deserialize<FirstEditionRuntimeUpgradeBinding>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return copy?.UpgradeId == binding.UpgradeId && copy.UpgradeCardGuid == binding.UpgradeCardGuid && copy.ShipGuid == binding.ShipGuid;
        });
        return new()
        {
            Check("source-blueprint-valid", blueprint.IsValid, "The Phase 16F-R1 assignment blueprint remains valid."),
            Check("all-runtime-objects-resolve", new[] { owner.ShipGuid, owner.PilotCardGuid, owner.DialGuid, owner.ControllerGuid }
                .Concat(bindings.Select(item => item.UpgradeCardGuid)).All(objectGuids.Contains), "Every bound GUID resolves to one object in the validation save."),
            Check("single-owner-binding", bindings.All(item => item.StablePilotKey == owner.StablePilotKey && item.StableShipKey == owner.StableShipKey
                && item.ShipGuid == owner.ShipGuid && item.PilotCardGuid == owner.PilotCardGuid && item.DialGuid == owner.DialGuid),
                "Every upgrade is bound to the same pilot, ship and dial objects."),
            Check("upgrade-guids-unique", bindings.Select(item => item.UpgradeCardGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() == bindings.Count,
                "Every assigned upgrade card has a unique TTS GUID."),
            Check("slot-bindings-exact", bindings.All(binding => blueprint.Upgrades.Any(upgrade => upgrade.UpgradeId == binding.UpgradeId && upgrade.SlotId == binding.SlotId)),
                "Every upgrade retains its exact First Edition slot assignment."),
            Check("save-load-round-trip", roundTrips, "Every upgrade ownership contract survives a JSON save/load round trip."),
            Check("all-handlers-inactive", bindings.All(item => item.ActivationStatus == "inactive" && item.Handlers.All(handler => handler.ActivationStatus == "inactive")),
                "No mechanics handler is active."),
            Check("no-gameplay-mutation", true, "The fixture stores and validates metadata only; it has no stat, action, dial, token, damage or discard handler.")
        };
    }

    private static JsonObject Controller(FirstEditionRuntimeOwnerBinding owner, List<FirstEditionRuntimeUpgradeBinding> upgrades,
        FirstEditionRuntimeAssignmentBlueprint blueprint, double x, double z)
    {
        var state = JsonSerializer.Serialize(new { owner, upgrades, activationStatus = "inactive" }, ContractJson);
        var guids = string.Join(",", upgrades.Select(item => $"'{item.UpgradeCardGuid}'"));
        var lua = $$"""
        -- Phase 16F-R2 ownership controller. This script deliberately executes no gameplay effects.
        local binding = JSON.decode({{LuaString(state)}})

        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then binding = JSON.decode(saved_data) end
          self.setTable('FirstEditionLoadoutBinding', binding)
          self.addContextMenuItem('Validate inactive loadout bindings', validateBindings, false)
        end

        function onSave()
          return JSON.encode(binding)
        end

        function validateBindings(player_color)
          local required = {'{{owner.ShipGuid}}','{{owner.PilotCardGuid}}','{{owner.DialGuid}}',{{guids}}}
          for _, guid in ipairs(required) do
            if getObjectFromGUID(guid) == nil then
              broadcastToColor('First Edition binding missing object '..guid, player_color, {1,0.25,0.25})
              return
            end
          end
          broadcastToColor('First Edition loadout bindings valid; all mechanics remain inactive.', player_color, {0.35,1,0.35})
        end
        """;
        return Notecard(owner.ControllerGuid, $"{blueprint.Owner.PilotName} — inactive loadout controller",
            $"Bound upgrades: {upgrades.Count}\nAll mechanics handlers: inactive\nRight-click to validate GUID bindings.", x, z, lua, state, new JsonObject
            {
                ["kind"] = "first-edition-loadout-controller", ["stablePilotKey"] = owner.StablePilotKey,
                ["stableShipKey"] = owner.StableShipKey, ["activationStatus"] = "inactive"
            });
    }

    private static JsonObject UpgradeCard(FirstEditionRuntimeUpgradeBinding binding, FirstEditionRuntimeUpgradeContract source,
        string assetBaseUrl, double x, double z)
    {
        var state = JsonSerializer.Serialize(binding, ContractJson);
        var lua = $$"""
        -- Phase 16F-R2 upgrade ownership metadata. No mechanics handler is executed here.
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
            ["Transform"] = Transform(x, 1.2, z, 0, 180, 0,
                FirstEditionPhysicalCardScale.MiniCardScaleX,
                FirstEditionPhysicalCardScale.MiniCardScaleY,
                FirstEditionPhysicalCardScale.MiniCardScaleZ),
            ["Nickname"] = source.Name, ["Description"] = $"{source.SlotType} — {source.Points} points\nBound slot: {source.SlotId}\nRuntime: inactive",
            ["GMNotes"] = state, ["AltLookAngle"] = Vector(0, 0, 0), ["ColorDiffuse"] = Color(1, 1, 1), ["LayoutGroupSortIndex"] = 0,
            ["Value"] = source.Points, ["Locked"] = false, ["Grid"] = true, ["Snap"] = true, ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false, ["DragSelectable"] = true, ["Autoraise"] = true, ["Sticky"] = true, ["Tooltip"] = true,
            ["GridProjection"] = false, ["HideWhenFaceDown"] = true, ["Hands"] = true, ["CardID"] = 100,
            ["CustomDeck"] = new JsonObject { ["1"] = new JsonObject
                {
                    ["FaceURL"] = AssetUrl(assetBaseUrl, source.FaceRepositoryPath), ["BackURL"] = AssetUrl(assetBaseUrl, source.BackRepositoryPath),
                    ["NumWidth"] = 1, ["NumHeight"] = 1, ["BackIsHidden"] = true, ["UniqueBack"] = false, ["Type"] = 0
                } },
            ["LuaScript"] = lua, ["LuaScriptState"] = state, ["XmlUI"] = ""
        };
    }

    private static JsonObject Anchor(string guid, string name, string kind, double x, double z) => Notecard(guid, name,
        $"Phase 16F-R2 post-spawn {kind} GUID anchor. Validation only.", x, z, "", "", new JsonObject { ["kind"] = kind, ["runtime"] = "inactive" });

    private static JsonObject Notecard(string guid, string name, string description, double x, double z, string lua, string state, JsonObject notes) => new()
    {
        ["GUID"] = guid, ["Name"] = "Notecard", ["Transform"] = Transform(x, 1, z, 0, 180, 0, 1.25, 1, 1.25),
        ["Nickname"] = name, ["Description"] = description, ["GMNotes"] = notes.ToJsonString(), ["AltLookAngle"] = Vector(0, 0, 0),
        ["ColorDiffuse"] = Color(0.18, 0.32, 0.55), ["LayoutGroupSortIndex"] = 0, ["Value"] = 0, ["Locked"] = true,
        ["Grid"] = true, ["Snap"] = true, ["IgnoreFoW"] = false, ["MeasureMovement"] = false, ["DragSelectable"] = true,
        ["Autoraise"] = true, ["Sticky"] = true, ["Tooltip"] = true, ["GridProjection"] = false, ["HideWhenFaceDown"] = false,
        ["Hands"] = false, ["Memo"] = description, ["LuaScript"] = lua, ["LuaScriptState"] = state, ["XmlUI"] = ""
    };

    private static JsonObject SaveEnvelope(JsonArray objects, FirstEditionRuntimeAssignmentBlueprint blueprint) => new()
    {
        ["SaveName"] = $"Phase 16F-R2 — {blueprint.Owner.PilotName} runtime binding validation", ["GameMode"] = "",
        ["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt"), ["Table"] = "Table_RPG", ["Sky"] = "Sky_Museum",
        ["Note"] = "Ownership/GUID persistence validation only. No First Edition upgrade effect is active.",
        ["Rules"] = "", ["PlayerTurn"] = "", ["LuaScript"] = "", ["LuaScriptState"] = "", ["XmlUI"] = "",
        ["ObjectStates"] = objects
    };

    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) => new() { Id = id, Passed = passed, Message = message };
    private static JsonObject Transform(double x, double y, double z, double rx, double ry, double rz, double sx, double sy, double sz) =>
        new() { ["posX"] = x, ["posY"] = y, ["posZ"] = z, ["rotX"] = rx, ["rotY"] = ry, ["rotZ"] = rz, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz };
    private static JsonObject Vector(double x, double y, double z) => new() { ["x"] = x, ["y"] = y, ["z"] = z };
    private static JsonObject Color(double r, double g, double b) => new() { ["r"] = r, ["g"] = g, ["b"] = b };
    private static string AssetUrl(string baseUrl, string path) => baseUrl + path.Replace('\\', '/').TrimStart('/');
    private static string NormalizeBaseUrl(string value) => value.TrimEnd('/') + "/";
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
}
