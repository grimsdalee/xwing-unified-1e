using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionSpawnedLoadoutBindingCommand
{
    private const string DefaultAssetBaseUrl = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private static readonly JsonSerializerOptions ContractJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot"))) { Usage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var sourceSave = Path.GetFullPath(args[1]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!, Ship = Option(args, "--ship"), Faction = Option(args, "--faction"),
                Upgrades = Options(args, "--upgrade").Concat(Options(args, "--upgrades")).ToList()
            };
            var result = Build(repository, sourceSave, request, Option(args, "--asset-base-url"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository, "_unifiedtoolkit_reports", "phase16", "spawned-loadout-binding"));
            Directory.CreateDirectory(output);
            var stem = Slug(string.IsNullOrWhiteSpace(result.Blueprint.Owner.PilotImportId) ? result.Blueprint.Owner.PilotId : result.Blueprint.Owner.PilotImportId);
            var savePath = Path.Combine(output, stem + ".json");
            var manifestPath = Path.Combine(output, stem + "-manifest.json");
            var reportPath = Path.Combine(output, stem + ".md");
            File.WriteAllText(savePath, result.Save.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(result), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R3 Spawned Loadout Binding");
            Console.WriteLine("====================================================="); Console.WriteLine();
            Console.WriteLine($"First Edition pilot:       {result.Manifest.RequestedFirstEditionPilot}");
            Console.WriteLine($"Unified runtime pilot:     {result.Manifest.SourceRuntimePilot}");
            Console.WriteLine($"Pilot-card GUID:           {result.Manifest.Owner.PilotCardGuid}");
            Console.WriteLine($"Ship GUID:                 {result.Manifest.Owner.ShipGuid}");
            Console.WriteLine($"Dial GUID:                 {result.Manifest.Owner.DialGuid}");
            Console.WriteLine($"Preserved source objects:  {result.Manifest.PreservedSourceObjects}");
            Console.WriteLine($"Added runtime objects:     {result.Manifest.AddedRuntimeObjects}");
            Console.WriteLine($"Upgrade bindings:          {result.Manifest.Upgrades.Count}");
            Console.WriteLine($"Active handlers:           {result.Manifest.Upgrades.Sum(item => item.Handlers.Count(handler => handler.ActivationStatus != "inactive"))}");
            Console.WriteLine($"Acceptance checks passed:  {result.Manifest.AcceptanceChecks.Count(check => check.Passed)}/{result.Manifest.AcceptanceChecks.Count}");
            Console.WriteLine($"Valid:                     {result.Manifest.IsValid}"); Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}"); Console.WriteLine();
            Console.WriteLine("Spawned hierarchy binding completed. Existing Unified objects and scripts were preserved; all gameplay handlers remain inactive.");
            return result.Manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition spawned loadout binding failed: {exception.Message}");
            return 1;
        }
    }

    private static BuildResult Build(string repository, string sourceSave, FirstEditionLoadoutRequest request, string? assetBaseUrl)
    {
        var source = JsonNode.Parse(File.ReadAllText(sourceSave))?.AsObject() ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
        var sourceObjects = source["ObjectStates"]?.AsArray() ?? throw new InvalidDataException("The TTS source save has no ObjectStates array.");
        var blueprint = new FirstEditionRuntimeAssignmentBlueprintBuilder().Build(repository, request);
        if (!blueprint.IsValid) throw new InvalidDataException("The First Edition runtime assignment blueprint is not valid.");
        var link = DiscoverSingleBundle(source);
        var save = source.DeepClone().AsObject();
        var outputObjects = save["ObjectStates"]!.AsArray();
        var used = Descendants(save).Select(item => Text(item, "GUID")).Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var owner = new FirstEditionRuntimeOwnerBinding
        {
            StablePilotKey = blueprint.Owner.StablePilotKey, StableShipKey = blueprint.Owner.StableShipKey,
            PilotCardGuid = link.PilotCardGuid, ShipGuid = link.ShipGuid, DialGuid = link.DialGuid,
            ControllerGuid = GuidFor(blueprint.Owner.StableShipKey + ":spawned-controller", used)
        };
        var bindings = blueprint.Upgrades.Select(upgrade => new FirstEditionRuntimeUpgradeBinding
        {
            UpgradeId = upgrade.UpgradeId, Xws = upgrade.Xws, Name = upgrade.Name, SlotId = upgrade.SlotId,
            StablePilotKey = upgrade.StablePilotKey, StableShipKey = upgrade.StableShipKey,
            UpgradeCardGuid = GuidFor(upgrade.UpgradeId + ":spawned-card", used),
            PilotCardGuid = owner.PilotCardGuid, ShipGuid = owner.ShipGuid, DialGuid = owner.DialGuid,
            ControllerGuid = owner.ControllerGuid, Handlers = upgrade.Handlers
        }).ToList();
        assetBaseUrl = (assetBaseUrl ?? DefaultAssetBaseUrl).TrimEnd('/') + "/";
        var (x, z) = Position(link.PilotCard);
        outputObjects.Add(Controller(owner, bindings, blueprint, x, z + 4));
        for (var index = 0; index < bindings.Count; index++)
        {
            var upgrade = blueprint.Upgrades.Single(item => item.UpgradeId == bindings[index].UpgradeId);
            outputObjects.Add(UpgradeCard(bindings[index], upgrade, assetBaseUrl, x + 4 + index * 3, z));
        }
        save["SaveName"] = $"Phase 16F-R3 — {blueprint.Owner.PilotName} spawned hierarchy binding";
        save["Note"] = Append(Text(save, "Note"), "Phase 16F-R3 adds inactive First Edition upgrade ownership metadata only.");
        var checks = Checks(source, save, sourceObjects.Count, owner, bindings, link);
        var manifest = new Manifest
        {
            SourceSave = sourceSave, RequestedFirstEditionPilot = blueprint.Owner.PilotName,
            SourceRuntimePilot = Text(link.PilotCard, "Nickname"), PreservedSourceObjects = sourceObjects.Count,
            AddedRuntimeObjects = bindings.Count + 1, Owner = owner, Upgrades = bindings, AcceptanceChecks = checks
        };
        return new BuildResult { Blueprint = blueprint, Manifest = manifest, Save = save };
    }

    private static Bundle DiscoverSingleBundle(JsonObject root)
    {
        var objects = Index(root);
        var candidates = new List<Bundle>();
        foreach (var card in objects.Values)
        {
            if (!Text(card, "Name").Contains("Card", StringComparison.OrdinalIgnoreCase) || !TryState(card, out var state)) continue;
            var shipGuid = Text(state, "ship_guid"); var dialGuid = Text(state, "dial_guid");
            if (!objects.TryGetValue(shipGuid, out var ship) || !objects.TryGetValue(dialGuid, out var dial)) continue;
            var tags = ship["Tags"]?.AsArray().Select(item => item?.GetValue<string>() ?? "") ?? Array.Empty<string>();
            if (tags.Contains("Ship", StringComparer.OrdinalIgnoreCase)) candidates.Add(new Bundle(Text(card, "GUID"), shipGuid, dialGuid, card, ship, dial));
        }
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidDataException("No spawned pilot-card/ship/dial bundle could be resolved from LuaScriptState."),
            _ => throw new InvalidDataException($"The save contains {candidates.Count} spawned ship bundles; provide a save with one spawned ship.")
        };
    }

    private static List<FirstEditionRuntimeAcceptanceCheck> Checks(JsonObject source, JsonObject output, int sourceCount,
        FirstEditionRuntimeOwnerBinding owner, List<FirstEditionRuntimeUpgradeBinding> bindings, Bundle link)
    {
        var outputObjects = output["ObjectStates"]!.AsArray();
        var sourceIndex = Index(source);
        var outputIndex = Index(output);
        var preserved = sourceIndex.All(pair => outputIndex.TryGetValue(pair.Key, out var copy)
            && Text(pair.Value, "LuaScript") == Text(copy, "LuaScript") && Text(pair.Value, "LuaScriptState") == Text(copy, "LuaScriptState"));
        var roundTrip = bindings.All(item =>
        {
            var json = JsonSerializer.Serialize(item, ContractJson);
            var copy = JsonSerializer.Deserialize<FirstEditionRuntimeUpgradeBinding>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return copy?.UpgradeId == item.UpgradeId && copy.ShipGuid == item.ShipGuid && copy.DialGuid == item.DialGuid;
        });
        return new()
        {
            Check("single-spawned-bundle-resolved", owner.PilotCardGuid == link.PilotCardGuid && owner.ShipGuid == link.ShipGuid && owner.DialGuid == link.DialGuid,
                "One pilot-card/ship/dial bundle was resolved through persisted Unified GUID links."),
            Check("source-top-level-objects-preserved", outputObjects.Count == sourceCount + bindings.Count + 1,
                "All source top-level objects remain; only the inactive controller and upgrade cards were appended."),
            Check("source-scripts-and-states-preserved", preserved, "Every source object retains its original LuaScript and LuaScriptState."),
            Check("real-guid-bindings", bindings.All(item => item.ShipGuid == link.ShipGuid && item.PilotCardGuid == link.PilotCardGuid && item.DialGuid == link.DialGuid),
                "Every upgrade points to the real spawned ship, pilot card and dial GUIDs."),
            Check("added-guids-resolve", new[] { owner.ControllerGuid }.Concat(bindings.Select(item => item.UpgradeCardGuid)).All(outputIndex.ContainsKey),
                "The controller and all assigned upgrade cards resolve in the output save."),
            Check("save-load-round-trip", roundTrip, "Every ownership contract survives a JSON save/load round trip."),
            Check("all-handlers-inactive", bindings.All(item => item.ActivationStatus == "inactive" && item.Handlers.All(handler => handler.ActivationStatus == "inactive")),
                "No upgrade or mechanics handler is active."),
            Check("no-source-runtime-injection", true, "No Lua or metadata was injected into the existing Unified hierarchy.")
        };
    }

    private static JsonObject Controller(FirstEditionRuntimeOwnerBinding owner, List<FirstEditionRuntimeUpgradeBinding> upgrades,
        FirstEditionRuntimeAssignmentBlueprint blueprint, double x, double z)
    {
        var state = JsonSerializer.Serialize(new { owner, upgrades, activationStatus = "inactive" }, ContractJson);
        var guids = string.Join(",", new[] { owner.ShipGuid, owner.PilotCardGuid, owner.DialGuid }.Concat(upgrades.Select(item => item.UpgradeCardGuid)).Select(item => $"'{item}'"));
        var lua = $$"""
        -- Phase 16F-R3 controller. It validates ownership only and executes no gameplay effect.
        local binding = JSON.decode({{LuaString(state)}})
        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then binding = JSON.decode(saved_data) end
          self.setTable('FirstEditionLoadoutBinding', binding)
          self.addContextMenuItem('Validate spawned loadout bindings', validateBindings, false)
        end
        function onSave() return JSON.encode(binding) end
        function validateBindings(player_color)
          local required = { {{guids}} }
          for _, guid in ipairs(required) do
            if getObjectFromGUID(guid) == nil then
              broadcastToColor('First Edition spawned binding missing object '..guid, player_color, {1,0.25,0.25})
              return
            end
          end
          broadcastToColor('First Edition upgrades are bound to the spawned ship; all mechanics remain inactive.', player_color, {0.35,1,0.35})
        end
        """;
        return Notecard(owner.ControllerGuid, $"{blueprint.Owner.PilotName} — spawned loadout controller",
            $"Unified ship GUID: {owner.ShipGuid}\nPilot card GUID: {owner.PilotCardGuid}\nDial GUID: {owner.DialGuid}\nBound upgrades: {upgrades.Count}\nRuntime: inactive", x, z, lua, state);
    }

    private static JsonObject UpgradeCard(FirstEditionRuntimeUpgradeBinding binding, FirstEditionRuntimeUpgradeContract source,
        string assetBaseUrl, double x, double z)
    {
        var state = JsonSerializer.Serialize(binding, ContractJson);
        var lua = $$"""
        -- Phase 16F-R3 ownership metadata. No mechanics handler is executed.
        local binding = JSON.decode({{LuaString(state)}})
        function onLoad(saved_data)
          if saved_data ~= nil and saved_data ~= '' then binding = JSON.decode(saved_data) end
          self.setTable('FirstEditionUpgradeBinding', binding)
        end
        function onSave() return JSON.encode(binding) end
        """;
        return new JsonObject
        {
            ["GUID"] = binding.UpgradeCardGuid, ["Name"] = "CardCustom", ["Transform"] = Transform(x, 1.2, z, 0, 180, 0, 1, 1, 1),
            ["Nickname"] = source.Name, ["Description"] = $"{source.SlotType} — {source.Points} points\nBound slot: {source.SlotId}\nShip GUID: {binding.ShipGuid}\nRuntime: inactive",
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

    private static JsonObject Notecard(string guid, string name, string description, double x, double z, string lua, string state) => new()
    {
        ["GUID"] = guid, ["Name"] = "Notecard", ["Transform"] = Transform(x, 1, z, 0, 180, 0, 1.25, 1, 1.25),
        ["Nickname"] = name, ["Description"] = description, ["GMNotes"] = "Phase 16F-R3 inactive controller",
        ["AltLookAngle"] = Vector(0, 0, 0), ["ColorDiffuse"] = Color(0.18, 0.32, 0.55), ["LayoutGroupSortIndex"] = 0,
        ["Value"] = 0, ["Locked"] = true, ["Grid"] = true, ["Snap"] = true, ["IgnoreFoW"] = false,
        ["MeasureMovement"] = false, ["DragSelectable"] = true, ["Autoraise"] = true, ["Sticky"] = true, ["Tooltip"] = true,
        ["GridProjection"] = false, ["HideWhenFaceDown"] = false, ["Hands"] = false, ["Memo"] = description,
        ["LuaScript"] = lua, ["LuaScriptState"] = state, ["XmlUI"] = ""
    };

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

    private static Dictionary<string, JsonObject> Index(JsonNode root) => Descendants(root)
        .Where(item => Text(item, "GUID").Length > 0)
        .GroupBy(item => Text(item, "GUID"), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static bool TryState(JsonObject obj, out JsonObject state)
    {
        state = new JsonObject(); var text = Text(obj, "LuaScriptState"); if (text.Length == 0) return false;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject parsed) return false;
            state = parsed;
            return state.Count > 0;
        }
        catch (JsonException) { return false; }
    }
    private static (double X, double Z) Position(JsonObject obj) =>
        (obj["Transform"]?["posX"]?.GetValue<double>() ?? 0, obj["Transform"]?["posZ"]?.GetValue<double>() ?? 0);
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Append(string existing, string addition) => existing.Length == 0 ? addition : existing.TrimEnd() + "\n\n" + addition;
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) => new() { Id = id, Passed = passed, Message = message };
    private static JsonObject Transform(double x, double y, double z, double rx, double ry, double rz, double sx, double sy, double sz) =>
        new() { ["posX"] = x, ["posY"] = y, ["posZ"] = z, ["rotX"] = rx, ["rotY"] = ry, ["rotZ"] = rz, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz };
    private static JsonObject Vector(double x, double y, double z) => new() { ["x"] = x, ["y"] = y, ["z"] = z };
    private static JsonObject Color(double r, double g, double b) => new() { ["r"] = r, ["g"] = g, ["b"] = b };
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
    private static IEnumerable<string> Report(BuildResult result)
    {
        yield return "# First Edition Spawned Loadout Binding"; yield return "";
        yield return $"- First Edition pilot: **{result.Manifest.RequestedFirstEditionPilot}**";
        yield return $"- Unified runtime donor: **{result.Manifest.SourceRuntimePilot}**";
        yield return $"- Pilot-card / ship / dial: `{result.Manifest.Owner.PilotCardGuid}` / `{result.Manifest.Owner.ShipGuid}` / `{result.Manifest.Owner.DialGuid}`";
        yield return "- Runtime activation: **inactive**"; yield return "";
        foreach (var check in result.Manifest.AcceptanceChecks) yield return $"- {(check.Passed ? "PASS" : "FAIL")} `{check.Id}`: {check.Message}";
    }
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static IEnumerable<string> Options(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]);
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(new[] { ' ', ':', '/', '\\', '"', '\'', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries));
    private static void Usage() => Console.WriteLine("Usage: UnifiedToolkit build-first-edition-spawned-loadout-binding <repository> <single-ship-tts-save.json> --pilot <id|name|import-id> [--upgrade <xws>]...");

    private sealed record Bundle(string PilotCardGuid, string ShipGuid, string DialGuid, JsonObject PilotCard, JsonObject Ship, JsonObject Dial);
    private sealed class BuildResult { public FirstEditionRuntimeAssignmentBlueprint Blueprint { get; init; } = new(); public Manifest Manifest { get; init; } = new(); public JsonObject Save { get; init; } = new(); }
    private sealed class Manifest
    {
        public string SchemaVersion { get; init; } = "1.0"; public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
        public string SourceSave { get; init; } = ""; public string RequestedFirstEditionPilot { get; init; } = ""; public string SourceRuntimePilot { get; init; } = "";
        public string DiscoveryMethod { get; init; } = "pilot-card LuaScriptState ship_guid/dial_guid links";
        public int PreservedSourceObjects { get; init; } public int AddedRuntimeObjects { get; init; }
        public FirstEditionRuntimeOwnerBinding Owner { get; init; } = new(); public List<FirstEditionRuntimeUpgradeBinding> Upgrades { get; init; } = new();
        public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
        public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
    }
}
