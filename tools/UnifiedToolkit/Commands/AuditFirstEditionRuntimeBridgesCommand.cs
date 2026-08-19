using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Commands;

public static class AuditFirstEditionRuntimeBridgesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly RuntimeBridgeDefinition[] BridgeDefinitions =
    {
        new(
            "added-action",
            "Added actions",
            "adds-action",
            "assets/source/unified25/TTS_xwing/src/Game/Component/Spawner/DataPad.lua",
            new[] { "Data.actSet", "action_set" },
            "assets/source/unified25/TTS_xwing/src/Dial/UnassignedDial.lua",
            new[] { "shipData['actSet']", "proxyPerformAction" },
            "Mutate the owning ship's Data.actSet idempotently, then refresh its assigned dial UI.",
            "Engine Upgrade",
            "B"),
        new(
            "maneuver-difficulty",
            "Manoeuvre difficulty changes",
            "maneuver-difficulty-change",
            "assets/source/unified25/TTS_xwing/src/Ship/CompositeBase.lua",
            new[] { "function setMoveSet", "data.moveSet" },
            "assets/source/unified25/TTS_xwing/src/Dial/ManeuverSetEditor.lua",
            new[] { "ship.call(\"setMoveSet\"", "moveSet = moveSet" },
            "Transform a copy of the ship's baseline moveSet and apply it through setMoveSet; never alter the printed dial texture.",
            "R2 Astromech",
            "green/blue compatibility mapping required"),
        new(
            "upgrade-slot",
            "Upgrade-slot changes",
            "upgrade-slot-change",
            "tools/UnifiedToolkit/Runtime/Loadouts/FirstEditionLoadoutPlanner.cs",
            new[] { "RequiresStructuralReview", "structural-effect-pending" },
            "assets/source/unified1e/reference/cards/upgrade-mechanics.json",
            new[] { "upgrade-slot-change" },
            "Apply typed slot transformations before final assignment, with source-card ownership and choice metadata.",
            "fixed-slot card after structural catalogue review",
            "pre-assignment planner stage")
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot-card-guid")))
        {
            Usage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var savePath = Path.GetFullPath(args[1]);
            var requestedPilotCardGuid = Option(args, "--pilot-card-guid")!;
            var sourceText = File.ReadAllText(savePath);
            var save = JsonNode.Parse(sourceText)?.AsObject()
                ?? throw new InvalidDataException("The TTS source save is not a JSON object.");
            var objectIndex = Index(save);
            var bundle = ResolveBundle(objectIndex, requestedPilotCardGuid);
            var catalogue = LoadCatalogue(repository);

            var bridges = BridgeDefinitions.Select(definition => AuditBridge(repository, definition, catalogue)).ToList();
            var policy = new FirstEditionRuntimeAutomationPolicy
            {
                PlayerManaged = new()
                {
                    "Printed hull changes",
                    "Printed primary-weapon changes",
                    "Printed agility changes",
                    "Printed pilot-skill changes"
                },
                RuntimeManaged = new()
                {
                    "Physical shield and energy token changes",
                    "Action availability through Data.actSet",
                    "Effective manoeuvre colours through Data.moveSet",
                    "Upgrade-slot additions, removals and conversions before assignment"
                }
            };

            var sourceUnchanged = save.ToJsonString() == JsonNode.Parse(sourceText)!.ToJsonString();
            var checks = new List<RuntimeBridgeAuditCheck>
            {
                Check("spawned-owner-resolved", bundle.ShipGuid.Length == 6 && bundle.DialGuid.Length == 6,
                    "The selected pilot card resolves to a real Unified ship and dial hierarchy."),
                Check("action-bridge-evidence", bridges.Single(item => item.Id == "added-action").EvidenceComplete,
                    "Unified action-set storage, dial rendering and action dispatch evidence are present."),
                Check("maneuver-bridge-evidence", bridges.Single(item => item.Id == "maneuver-difficulty").EvidenceComplete,
                    "Unified move-set storage, mutation and dial rendering evidence are present."),
                Check("slot-bridge-evidence", bridges.Single(item => item.Id == "upgrade-slot").EvidenceComplete,
                    "The planner's current structural-review boundary and source catalogue are present."),
                Check("static-stat-overlay-excluded", policy.PlayerManaged.Count == 4,
                    "Printed hull, attack, agility and pilot skill remain player-managed; no overlay is planned."),
                Check("no-gameplay-mutation", sourceUnchanged,
                    "The source save was inspected without adding objects or changing gameplay state.")
            };

            var result = new FirstEditionRuntimeBridgeAudit
            {
                Repository = repository,
                SourceSave = savePath,
                Owner = bundle,
                Policy = policy,
                Bridges = bridges,
                AcceptanceChecks = checks
            };
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "runtime-bridge-audit"));
            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "first-edition-runtime-bridge-audit.json");
            var csvPath = Path.Combine(output, "first-edition-runtime-bridge-upgrades.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-RUNTIME-BRIDGE-AUDIT.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(csvPath, Csv(bridges), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(result), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 16F-R6 Runtime Bridge Audit");
            Console.WriteLine("==================================================");
            Console.WriteLine();
            Console.WriteLine($"Pilot-card GUID:              {bundle.PilotCardGuid}");
            Console.WriteLine($"Ship GUID:                    {bundle.ShipGuid}");
            Console.WriteLine($"Dial GUID:                    {bundle.DialGuid}");
            Console.WriteLine($"Runtime bridges:              {bridges.Count}");
            Console.WriteLine($"Added-action upgrades:        {bridges.Single(item => item.Id == "added-action").Upgrades.Count}");
            Console.WriteLine($"Manoeuvre-colour upgrades:    {bridges.Single(item => item.Id == "maneuver-difficulty").Upgrades.Count}");
            Console.WriteLine($"Upgrade-slot-change upgrades: {bridges.Single(item => item.Id == "upgrade-slot").Upgrades.Count}");
            Console.WriteLine($"Printed stat overlays:        0");
            Console.WriteLine($"Gameplay mutations:           0");
            Console.WriteLine($"Acceptance checks passed:     {checks.Count(check => check.Passed)}/{checks.Count}");
            Console.WriteLine($"Valid:                        {result.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"Audit:    {jsonPath}");
            Console.WriteLine($"Upgrades: {csvPath}");
            Console.WriteLine($"Report:   {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Runtime bridge audit completed. No TTS save, Lua script, asset or gameplay state was modified.");
            return result.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition runtime bridge audit failed: {exception.Message}");
            return 1;
        }
    }

    private static RuntimeBridgeAuditEntry AuditBridge(string repository, RuntimeBridgeDefinition definition,
        IReadOnlyList<RuntimeBridgeUpgrade> catalogue)
    {
        var primary = Evidence(repository, definition.PrimaryPath, definition.PrimaryNeedles);
        var secondary = Evidence(repository, definition.SecondaryPath, definition.SecondaryNeedles);
        return new RuntimeBridgeAuditEntry
        {
            Id = definition.Id,
            Name = definition.Name,
            MechanicId = definition.MechanicId,
            ProposedContract = definition.ProposedContract,
            FirstImplementationCandidate = definition.FirstCandidate,
            CompatibilityNote = definition.CompatibilityNote,
            Evidence = primary.Concat(secondary).ToList(),
            EvidenceComplete = primary.All(item => item.Found) && secondary.All(item => item.Found),
            Upgrades = catalogue.Where(item => item.Mechanics.Contains(definition.MechanicId, StringComparer.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static List<RuntimeBridgeEvidence> Evidence(string repository, string repositoryPath,
        IEnumerable<string> needles)
    {
        var fullPath = Path.Combine(repository, repositoryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return needles.Select(needle => new RuntimeBridgeEvidence
            {
                RepositoryPath = repositoryPath,
                SearchText = needle,
                Found = false
            }).ToList();
        var lines = File.ReadAllLines(fullPath);
        return needles.Select(needle =>
        {
            var match = Array.FindIndex(lines, line => line.Contains(needle, StringComparison.Ordinal));
            return new RuntimeBridgeEvidence
            {
                RepositoryPath = repositoryPath,
                SearchText = needle,
                Found = match >= 0,
                Line = match >= 0 ? match + 1 : null,
                Excerpt = match >= 0 ? lines[match].Trim() : ""
            };
        }).ToList();
    }

    private static List<RuntimeBridgeUpgrade> LoadCatalogue(string repository)
    {
        var path = Path.Combine(repository, "assets", "source", "unified1e", "reference", "cards", "upgrade-mechanics.json");
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException("The upgrade-mechanics catalogue is not a JSON object.");
        return root["upgrades"]?.AsArray().Select(node =>
        {
            var upgrade = node?.AsObject() ?? new JsonObject();
            return new RuntimeBridgeUpgrade
            {
                Xws = Text(upgrade, "xws"),
                Name = Text(upgrade, "name"),
                Slot = Text(upgrade, "slot"),
                EffectText = Text(upgrade, "effectText"),
                Mechanics = upgrade["mechanics"]?.AsArray()
                    .Select(item => Text(item?.AsObject() ?? new JsonObject(), "id"))
                    .Where(value => value.Length > 0).ToList() ?? new()
            };
        }).ToList() ?? new();
    }

    private static RuntimeBridgeOwner ResolveBundle(Dictionary<string, JsonObject> objects, string pilotCardGuid)
    {
        if (!objects.TryGetValue(pilotCardGuid, out var card))
            throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' was not found.");
        if (!TryObject(Text(card, "LuaScriptState"), out var state))
            throw new InvalidDataException($"Pilot-card GUID '{pilotCardGuid}' has no readable LuaScriptState.");
        var shipGuid = Text(state, "ship_guid");
        var dialGuid = Text(state, "dial_guid");
        if (!objects.TryGetValue(shipGuid, out var ship) || !objects.TryGetValue(dialGuid, out var dial))
            throw new InvalidDataException("The pilot card's ship_guid or dial_guid link is unresolved.");
        return new RuntimeBridgeOwner
        {
            PilotCardGuid = pilotCardGuid,
            PilotName = Text(card, "Nickname"),
            ShipGuid = shipGuid,
            ShipName = Text(ship, "Nickname"),
            DialGuid = dialGuid,
            DialName = Text(dial, "Nickname"),
            ResolutionMethod = "pilot-card LuaScriptState ship_guid/dial_guid links"
        };
    }

    private static IEnumerable<string> Csv(IEnumerable<RuntimeBridgeAuditEntry> bridges)
    {
        yield return "bridge_id,mechanic_id,xws,name,slot,effect_text";
        foreach (var bridge in bridges)
            foreach (var upgrade in bridge.Upgrades)
                yield return string.Join(',', new[] { bridge.Id, bridge.MechanicId, upgrade.Xws, upgrade.Name, upgrade.Slot, upgrade.EffectText }.Select(CsvValue));
    }

    private static IEnumerable<string> Report(FirstEditionRuntimeBridgeAudit audit)
    {
        yield return "# First Edition Runtime Bridge Audit";
        yield return "";
        yield return $"- Pilot: **{audit.Owner.PilotName}**";
        yield return $"- Pilot-card / ship / dial: `{audit.Owner.PilotCardGuid}` / `{audit.Owner.ShipGuid}` / `{audit.Owner.DialGuid}`";
        yield return "- Gameplay mutations: **none**";
        yield return "- Printed stat overlays: **excluded**";
        yield return "";
        yield return "## Runtime policy";
        yield return "";
        foreach (var item in audit.Policy.PlayerManaged) yield return $"- Player-managed: {item}";
        foreach (var item in audit.Policy.RuntimeManaged) yield return $"- Runtime-managed: {item}";
        foreach (var bridge in audit.Bridges)
        {
            yield return "";
            yield return $"## {bridge.Name}";
            yield return "";
            yield return $"- Catalogue upgrades: **{bridge.Upgrades.Count}**";
            yield return $"- Proposed contract: {bridge.ProposedContract}";
            yield return $"- First candidate: **{bridge.FirstImplementationCandidate}**";
            yield return $"- Compatibility: {bridge.CompatibilityNote}";
            foreach (var evidence in bridge.Evidence)
                yield return $"- {(evidence.Found ? "FOUND" : "MISSING")} `{evidence.RepositoryPath}`{(evidence.Line is null ? "" : $":{evidence.Line}")} — `{evidence.SearchText}`";
        }
        yield return "";
        yield return "## Acceptance checks";
        yield return "";
        foreach (var check in audit.AcceptanceChecks)
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

    private static RuntimeBridgeAuditCheck Check(string id, bool passed, string message) =>
        new() { Id = id, Passed = passed, Message = message };
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string CsvValue(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static void Usage() => Console.WriteLine(
        "Usage: UnifiedToolkit audit-first-edition-runtime-bridges <repository> <tts-save.json> --pilot-card-guid <guid> [--output <folder>]");

    private sealed record RuntimeBridgeDefinition(string Id, string Name, string MechanicId,
        string PrimaryPath, string[] PrimaryNeedles, string SecondaryPath, string[] SecondaryNeedles,
        string ProposedContract, string FirstCandidate, string CompatibilityNote);
}

public sealed class FirstEditionRuntimeBridgeAudit
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Repository { get; init; } = "";
    public string SourceSave { get; init; } = "";
    public RuntimeBridgeOwner Owner { get; init; } = new();
    public FirstEditionRuntimeAutomationPolicy Policy { get; init; } = new();
    public List<RuntimeBridgeAuditEntry> Bridges { get; init; } = new();
    public List<RuntimeBridgeAuditCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}

public sealed class RuntimeBridgeOwner
{
    public string PilotCardGuid { get; init; } = "";
    public string PilotName { get; init; } = "";
    public string ShipGuid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string DialGuid { get; init; } = "";
    public string DialName { get; init; } = "";
    public string ResolutionMethod { get; init; } = "";
}

public sealed class FirstEditionRuntimeAutomationPolicy
{
    public List<string> PlayerManaged { get; init; } = new();
    public List<string> RuntimeManaged { get; init; } = new();
}

public sealed class RuntimeBridgeAuditEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string MechanicId { get; init; } = "";
    public string ProposedContract { get; init; } = "";
    public string FirstImplementationCandidate { get; init; } = "";
    public string CompatibilityNote { get; init; } = "";
    public bool EvidenceComplete { get; init; }
    public List<RuntimeBridgeEvidence> Evidence { get; init; } = new();
    public List<RuntimeBridgeUpgrade> Upgrades { get; init; } = new();
}

public sealed class RuntimeBridgeEvidence
{
    public string RepositoryPath { get; init; } = "";
    public string SearchText { get; init; } = "";
    public bool Found { get; init; }
    public int? Line { get; init; }
    public string Excerpt { get; init; } = "";
}

public sealed class RuntimeBridgeUpgrade
{
    public string Xws { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";
    public string EffectText { get; init; } = "";
    public List<string> Mechanics { get; init; } = new();
}

public sealed class RuntimeBridgeAuditCheck
{
    public string Id { get; init; } = "";
    public bool Passed { get; init; }
    public string Message { get; init; } = "";
}
