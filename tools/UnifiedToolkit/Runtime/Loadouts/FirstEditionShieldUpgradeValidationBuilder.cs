using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionShieldUpgradeValidationBuilder
{
    private static readonly JsonSerializerOptions ContractJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public FirstEditionShieldUpgradeValidationResult Build(string repository, string sourceSavePath,
        FirstEditionLoadoutRequest pilotRequest, string activePilotCardGuid, string controlPilotCardGuid,
        string? assetBaseUrl = null)
    {
        if (activePilotCardGuid.Equals(controlPilotCardGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Active and control pilot-card GUIDs must be different.");

        var sourceText = File.ReadAllText(sourceSavePath);
        var source = JsonNode.Parse(sourceText)?.AsObject()
            ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
        var sourceIndex = Index(source);
        var activeOwnerSource = ResolveOwner(sourceIndex, activePilotCardGuid);
        var controlOwner = ResolveOwner(sourceIndex, controlPilotCardGuid);
        if (activeOwnerSource.ShipGuid == controlOwner.ShipGuid)
            throw new InvalidDataException("Active and control pilot cards resolve to the same ship.");

        var activeExistingShields = NearbyShieldGuids(source, sourceIndex[activePilotCardGuid]);
        var controlExistingShields = NearbyShieldGuids(source, sourceIndex[controlPilotCardGuid]);
        if (activeExistingShields.Count == 0 || controlExistingShields.Count == 0)
            throw new InvalidDataException("Both active and control ship bundles must contain their normal shield-token rows.");

        var request = new FirstEditionLoadoutRequest
        {
            Pilot = pilotRequest.Pilot,
            Ship = pilotRequest.Ship,
            Faction = pilotRequest.Faction,
            Upgrades = new() { FirstEditionShieldUpgradeHandler.UpgradeXws }
        };
        var registration = new FirstEditionProductionLoadoutRegistrar().Register(
            repository, source, request, activePilotCardGuid, assetBaseUrl);
        var upgrade = registration.Blueprint.Upgrades.Single();
        var handler = upgrade.Handlers.SingleOrDefault(item =>
            item.MechanicId == FirstEditionShieldUpgradeHandler.MechanicId)
            ?? throw new InvalidDataException("Shield Upgrade does not expose the expected stat-change mechanic contract.");
        var binding = registration.Manifest.Upgrades.Single();
        var output = registration.Save;
        var token = new FirstEditionShieldUpgradeHandler().Apply(
            output, registration.Manifest.Owner, registration.Blueprint.Owner.PilotName, assetBaseUrl);
        var outputIndex = Index(output);
        ActivateMetadata(outputIndex[registration.Manifest.Owner.ControllerGuid],
            outputIndex[binding.UpgradeCardGuid], registration.Manifest.Owner, binding, token);

        output["SaveName"] = "Phase 16F-R10 — Shield Upgrade physical-resource validation";
        output["Note"] = Append(Text(output, "Note"),
            "Phase 16F-R10 validates Shield Upgrade by adding one canonical, flippable First Edition shield token to the selected ship's existing shield row. The second ship remains unchanged.");

        var outputAfter = Index(output);
        var activeAfterShields = NearbyShieldGuids(output, outputAfter[activePilotCardGuid]);
        var controlAfterShields = NearbyShieldGuids(output, outputAfter[controlPilotCardGuid]);
        var sourcePreserved = sourceIndex.All(pair => outputAfter.TryGetValue(pair.Key, out var copy)
            && Text(pair.Value, "LuaScript") == Text(copy, "LuaScript")
            && Text(pair.Value, "LuaScriptState") == Text(copy, "LuaScriptState"));
        var tokenObject = outputAfter[token.TokenGuid];
        var checks = new List<FirstEditionRuntimeAcceptanceCheck>
        {
            Check("shield-upgrade-bound-to-modification", upgrade.SlotType == "Modification"
                && upgrade.Xws == FirstEditionShieldUpgradeHandler.UpgradeXws,
                "Shield Upgrade occupies the selected ship's Modification slot."),
            Check("single-active-handler", handler.MechanicId == FirstEditionShieldUpgradeHandler.MechanicId,
                "Only Shield Upgrade's reviewed physical shield-resource interpretation is active."),
            Check("one-additional-active-shield", activeAfterShields.Count == activeExistingShields.Count + 1
                && activeAfterShields.Contains(token.TokenGuid, StringComparer.OrdinalIgnoreCase),
                "The active ship has exactly one additional shield token."),
            Check("control-shield-row-unchanged", controlAfterShields.Count == controlExistingShields.Count
                && controlAfterShields.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(controlExistingShields),
                "The control ship's shield-token row is unchanged."),
            Check("canonical-first-edition-assets", Text(tokenObject["CustomMesh"]?.AsObject(), "MeshURL")
                    .EndsWith(token.MeshRepositoryPath, StringComparison.Ordinal)
                && Text(tokenObject["CustomMesh"]?.AsObject(), "DiffuseURL")
                    .EndsWith(token.FaceRepositoryPath, StringComparison.Ordinal),
                "The added token uses the approved First Edition shield mesh and face."),
            Check("unified-flip-handler-preserved", token.PreservesUnifiedFlipHandler
                && Text(tokenObject, "LuaScript").Contains("function onFlip", StringComparison.Ordinal)
                && Text(tokenObject, "LuaScript").Contains("shield_owner", StringComparison.Ordinal)
                && Text(tokenObject, "LuaScript").IndexOf("function onLoad", StringComparison.Ordinal)
                    < Text(tokenObject, "LuaScript").LastIndexOf("return __bundle_require(", StringComparison.Ordinal),
                "The added token retains Unified shield flip reporting, with owner initialization before the bundle's final return."),
            Check("explicit-owner-binding", TryObject(Text(tokenObject, "GMNotes"), out var notes)
                && Text(notes, "shipGuid") == registration.Manifest.Owner.ShipGuid
                && Text(notes, "pilotCardGuid") == registration.Manifest.Owner.PilotCardGuid
                && Text(notes, "sourceUpgradeXws") == FirstEditionShieldUpgradeHandler.UpgradeXws,
                "The additional shield token is explicitly bound to its ship, pilot card and source upgrade."),
            Check("row-spacing-preserved", IsAtRowEnd(outputAfter, activePilotCardGuid,
                    activeExistingShields, token.TokenGuid),
                "The new token continues the existing pilot-relative shield row at 0.9-unit spacing."),
            Check("source-hierarchy-preserved", sourcePreserved,
                "Every pre-existing Unified object retains its Lua script and saved runtime state."),
            Check("bounded-object-additions", outputAfter.Count == sourceIndex.Count + 3,
                "Only the hidden controller, Shield Upgrade card and one shield token are added."),
            Check("no-printed-stat-overlay", !output.ToJsonString().Contains("stat overlay", StringComparison.OrdinalIgnoreCase),
                "No printed shield or other statistic is graphically overlaid."),
            Check("source-input-unmodified", source.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString(),
                "The input save object was not modified in memory.")
        };

        return new FirstEditionShieldUpgradeValidationResult
        {
            Save = output,
            Manifest = new FirstEditionShieldUpgradeValidationManifest
            {
                PilotName = registration.Blueprint.Owner.PilotName,
                ActiveOwner = registration.Manifest.Owner,
                ControlPilotCardGuid = controlOwner.PilotCardGuid,
                ControlShipGuid = controlOwner.ShipGuid,
                UpgradeCardGuid = binding.UpgradeCardGuid,
                ShieldToken = token,
                ActiveExistingShieldCount = activeExistingShields.Count,
                ActiveFinalShieldCount = activeAfterShields.Count,
                ControlShieldCount = controlAfterShields.Count,
                ActiveHandlerCount = 1,
                AcceptanceChecks = checks
            }
        };
    }

    private static void ActivateMetadata(JsonObject controller, JsonObject card,
        FirstEditionRuntimeOwnerBinding owner, FirstEditionRuntimeUpgradeBinding binding,
        FirstEditionShieldTokenResult token)
    {
        controller["LuaScriptState"] = JsonSerializer.Serialize(new
        {
            owner,
            upgrades = new[] { binding },
            activationStatus = "active-physical-resource",
            activeHandlerId = "stat-change:shield-upgrade:physical-token",
            shieldTokenGuid = token.TokenGuid
        }, ContractJson);
        controller["Description"] = "Active First Edition physical-resource controller — Shield Upgrade only";
        if (TryObject(Text(controller, "GMNotes"), out var controllerNotes))
        {
            controllerNotes["activationStatus"] = "active-physical-resource";
            controllerNotes["activeHandlerId"] = "stat-change:shield-upgrade:physical-token";
            controllerNotes["shieldTokenGuid"] = token.TokenGuid;
            controller["GMNotes"] = controllerNotes.ToJsonString();
        }

        if (!TryObject(Text(card, "GMNotes"), out var cardNotes))
            throw new InvalidDataException("Shield Upgrade card metadata is not readable.");
        cardNotes["activationStatus"] = "active-physical-resource";
        cardNotes["shieldTokenGuid"] = token.TokenGuid;
        if (cardNotes["handlers"] is JsonArray handlers)
            foreach (var item in handlers.OfType<JsonObject>())
                item["activationStatus"] = Text(item, "mechanicId") == FirstEditionShieldUpgradeHandler.MechanicId
                    ? "active-physical-token" : "inactive";
        card["GMNotes"] = cardNotes.ToJsonString();
        card["LuaScriptState"] = cardNotes.ToJsonString();
        card["Description"] = "Modification — Shield Upgrade\nRuntime: active — adds one owned shield token";
    }

    private static Owner ResolveOwner(Dictionary<string, JsonObject> index, string pilotCardGuid)
    {
        if (!index.TryGetValue(pilotCardGuid, out var card) || !TryObject(Text(card, "LuaScriptState"), out var state))
            throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' is missing or has no readable runtime state.");
        var shipGuid = Text(state, "ship_guid");
        var dialGuid = Text(state, "dial_guid");
        if (!index.ContainsKey(shipGuid) || !index.ContainsKey(dialGuid))
            throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' does not resolve to a complete ship hierarchy.");
        return new Owner(pilotCardGuid, shipGuid, dialGuid);
    }

    private static List<string> NearbyShieldGuids(JsonObject save, JsonObject pilotCard)
    {
        var px = Number(pilotCard, "Transform", "posX");
        var pz = Number(pilotCard, "Transform", "posZ");
        return save["ObjectStates"]!.AsArray().OfType<JsonObject>()
            .Where(item => Text(item, "Nickname").Equals("Shield", StringComparison.OrdinalIgnoreCase))
            .Where(item => Distance(Number(item, "Transform", "posX"), Number(item, "Transform", "posZ"), px, pz) <= 4.0)
            .Select(item => Text(item, "GUID")).Where(guid => guid.Length > 0).ToList();
    }

    private static bool IsAtRowEnd(Dictionary<string, JsonObject> index, string pilotCardGuid,
        List<string> existingGuids, string newGuid)
    {
        var card = index[pilotCardGuid];
        var angle = Number(card, "Transform", "rotY") * Math.PI / 180.0;
        var rowX = Math.Cos(angle); var rowZ = Math.Sin(angle);
        var px = Number(card, "Transform", "posX"); var pz = Number(card, "Transform", "posZ");
        double Projection(string guid) =>
            (Number(index[guid], "Transform", "posX") - px) * rowX
            + (Number(index[guid], "Transform", "posZ") - pz) * rowZ;
        var previousEnd = existingGuids.Max(guid => Projection(guid));
        return Math.Abs((Projection(newGuid) - previousEnd) - FirstEditionShieldUpgradeHandler.TokenSpacing) < 0.01;
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
        value = new JsonObject(); if (text.Length == 0) return false;
        try { if (JsonNode.Parse(text) is not JsonObject parsed) return false; value = parsed; return true; }
        catch (JsonException) { return false; }
    }
    private static double Number(JsonObject obj, string parent, string key) => obj[parent]?[key]?.GetValue<double>() ?? 0;
    private static string Text(JsonObject? obj, string key) => obj?[key]?.GetValue<string>() ?? "";
    private static double Distance(double x1, double z1, double x2, double z2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(z1 - z2, 2));
    private static string Append(string existing, string addition) => existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) =>
        new() { Id = id, Passed = passed, Message = message };
    private sealed record Owner(string PilotCardGuid, string ShipGuid, string DialGuid);
}

public sealed class FirstEditionShieldUpgradeValidationResult
{
    public JsonObject Save { get; init; } = new();
    public FirstEditionShieldUpgradeValidationManifest Manifest { get; init; } = new();
}

public sealed class FirstEditionShieldUpgradeValidationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Shield Upgrade adds one physical owned shield token; printed statistics remain player-managed.";
    public string PilotName { get; init; } = "";
    public FirstEditionRuntimeOwnerBinding ActiveOwner { get; init; } = new();
    public string ControlPilotCardGuid { get; init; } = "";
    public string ControlShipGuid { get; init; } = "";
    public string UpgradeCardGuid { get; init; } = "";
    public FirstEditionShieldTokenResult ShieldToken { get; init; } = new();
    public int ActiveExistingShieldCount { get; init; }
    public int ActiveFinalShieldCount { get; init; }
    public int ControlShieldCount { get; init; }
    public int ActiveHandlerCount { get; init; }
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}
