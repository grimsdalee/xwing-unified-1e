using System.Text.Json;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionRuntimeAssignmentBlueprintBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FirstEditionRuntimeAssignmentBlueprint Build(string repository, FirstEditionLoadoutRequest request)
    {
        repository = Path.GetFullPath(repository);
        var plan = new FirstEditionLoadoutPlanner().Plan(repository, request);
        var mechanics = Read<MechanicsDocument>(repository, "assets", "source", "unified1e", "reference", "cards", "upgrade-mechanics.json")
            .Upgrades.GroupBy(item => Key(item.Xws)).ToDictionary(group => group.Key, group => group.First());

        var pilotIdentity = string.IsNullOrWhiteSpace(plan.Pilot.ImportId) ? plan.Pilot.Id : plan.Pilot.ImportId;
        var stablePilotKey = $"pilot:{Key(plan.Pilot.Faction)}:{Key(plan.Pilot.ShipId)}:{Key(pilotIdentity)}";
        var stableShipKey = $"ship:{Key(plan.Pilot.Faction)}:{Key(plan.Ship.Id)}:{Key(pilotIdentity)}";
        var upgrades = plan.Assignments.Where(assignment => assignment.IsAssigned).Select(assignment =>
        {
            mechanics.TryGetValue(Key(assignment.Xws), out var catalogue);
            var handlers = (catalogue?.Mechanics ?? new()).Select(mechanic => new FirstEditionRuntimeHandlerContract
            {
                HandlerId = $"mechanic:{mechanic.Id}", MechanicId = mechanic.Id, Name = mechanic.Name,
                ReviewStatus = mechanic.ReviewStatus, CatalogueRuntimeStatus = mechanic.RuntimeStatus,
                Evidence = mechanic.Evidence.ToList()
            }).OrderBy(handler => handler.MechanicId, StringComparer.OrdinalIgnoreCase).ToList();
            var stateRequirements = handlers.SelectMany(handler => StateRequirements(handler.MechanicId))
                .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase).OrderBy(item => item.Id).ToList();
            var dependencies = assignment.Conditions.Select(condition => new FirstEditionRuntimeDependency
            {
                Kind = "condition", Id = condition.Xws, Name = condition.Name,
                FaceRepositoryPath = condition.FaceRepositoryPath, BackRepositoryPath = condition.BackRepositoryPath,
                TokenRepositoryPath = condition.TokenRepositoryPath
            }).Concat(handlers.SelectMany(handler => MechanicDependencies(handler.MechanicId)))
                .DistinctBy(item => $"{item.Kind}|{item.Id}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.Kind).ThenBy(item => item.Id).ToList();
            return new FirstEditionRuntimeUpgradeContract
            {
                RequestIndex = assignment.RequestIndex,
                UpgradeId = $"upgrade:{Key(pilotIdentity)}:{assignment.AssignedSlotId}:{Key(assignment.Xws)}",
                Xws = assignment.Xws, Name = assignment.Name, SlotId = assignment.AssignedSlotId!, SlotType = assignment.Slot,
                Points = assignment.Points, Unique = assignment.Unique, Limited = assignment.Limited,
                FaceRepositoryPath = assignment.FaceRepositoryPath, BackRepositoryPath = assignment.BackRepositoryPath,
                EffectText = catalogue?.EffectText ?? "", RuntimePriority = catalogue?.RuntimePriority ?? "",
                RuntimePriorityReason = catalogue?.RuntimePriorityReason ?? "", StablePilotKey = stablePilotKey,
                StableShipKey = stableShipKey, RestrictedShips = catalogue?.RestrictedShips.ToList() ?? new(),
                RestrictedFactions = catalogue?.RestrictedFactions.ToList() ?? new(),
                RestrictedSizes = catalogue?.RestrictedSizes.ToList() ?? new(), Handlers = handlers,
                StateRequirements = stateRequirements, Dependencies = dependencies
            };
        }).ToList();

        var slots = plan.Slots.Select(slot => new FirstEditionRuntimeSlotContract
        {
            SlotId = slot.SlotId, Type = slot.Type, Ordinal = slot.Ordinal, Source = slot.Source,
            AssignedUpgradeXws = slot.AssignedUpgradeXws
        }).ToList();
        var checks = AcceptanceChecks(plan, upgrades, stablePilotKey, stableShipKey);
        return new FirstEditionRuntimeAssignmentBlueprint
        {
            Owner = new FirstEditionRuntimeOwnerContract
            {
                PilotId = plan.Pilot.Id, PilotImportId = plan.Pilot.ImportId, PilotName = plan.Pilot.Name,
                ShipId = plan.Ship.Id, ShipName = plan.Ship.Name, Faction = plan.Pilot.Faction,
                BaseSize = plan.Ship.Size, StablePilotKey = stablePilotKey, StableShipKey = stableShipKey
            },
            Slots = slots, Upgrades = upgrades,
            Cost = new FirstEditionRuntimeAssignmentCost { Pilot = plan.PilotCost, Upgrades = plan.UpgradeCost, Total = plan.TotalCost },
            AcceptanceChecks = checks, LoadoutIssues = plan.Issues, SourceLoadoutValid = plan.IsValid
        };
    }

    private static List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks(FirstEditionLoadoutPlan plan,
        List<FirstEditionRuntimeUpgradeContract> upgrades, string pilotKey, string shipKey) => new()
    {
        Check("source-loadout-valid", plan.IsValid, "The source First Edition loadout plan is valid."),
        Check("all-assignments-bound-to-slots", plan.Assignments.Count == upgrades.Count && upgrades.All(item => item.SlotId.Length > 0),
            "Every requested upgrade is assigned to one exact slot instance."),
        Check("stable-upgrade-identities-unique", upgrades.Select(item => item.UpgradeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == upgrades.Count,
            "Every upgrade has a unique stable identity."),
        Check("single-owner-contract", upgrades.All(item => item.StablePilotKey == pilotKey && item.StableShipKey == shipKey),
            "Every upgrade shares the pilot and ship ownership contract."),
        Check("all-handlers-inactive", upgrades.All(item => item.ActivationStatus == "inactive" && item.Handlers.All(handler => handler.ActivationStatus == "inactive")),
            "No upgrade effect or mechanics handler is active."),
        Check("guid-binding-deferred", upgrades.All(item => item.UpgradeCardGuid is null),
            "TTS GUID binding remains deferred until post-ship-spawn integration.")
    };

    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) =>
        new() { Id = id, Passed = passed, Message = message };

    private static IEnumerable<FirstEditionRuntimeStateRequirement> StateRequirements(string mechanicId)
    {
        if (mechanicId == "card-state-change") yield return State("discard-flip-state", mechanicId);
        if (mechanicId == "once-per-round-or-limited-use") yield return State("round-readiness-state", mechanicId);
        if (mechanicId == "upgrade-token-or-persistence") yield return State("upgrade-persistence-token-state", mechanicId);
    }

    private static FirstEditionRuntimeStateRequirement State(string id, string mechanicId) =>
        new() { Id = id, SourceMechanicId = mechanicId };

    private static IEnumerable<FirstEditionRuntimeDependency> MechanicDependencies(string mechanicId)
    {
        if (mechanicId == "bomb-or-mine") yield return Dependency("device", "device-selected-by-card");
        if (mechanicId == "token-assignment") yield return Dependency("gameplay-token", "token-selected-by-card");
        if (mechanicId == "condition-assignment") yield return Dependency("condition", "condition-selected-by-card");
        if (mechanicId == "upgrade-token-or-persistence") yield return Dependency("gameplay-token", "upgrade-persistence-token");
    }

    private static FirstEditionRuntimeDependency Dependency(string kind, string id) =>
        new() { Kind = kind, Id = id, Name = "Resolved by the reviewed card handler" };

    private static T Read<T>(string repository, params string[] parts)
    {
        var path = Path.Combine(new[] { repository }.Concat(parts).ToArray());
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize {path}.");
    }

    private static string Key(string? value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private sealed class MechanicsDocument { public List<MechanicsUpgrade> Upgrades { get; init; } = new(); }
    private sealed class MechanicsUpgrade
    {
        public string Xws { get; init; } = "";
        public string EffectText { get; init; } = "";
        public string RuntimePriority { get; init; } = "";
        public string RuntimePriorityReason { get; init; } = "";
        public List<string> RestrictedShips { get; init; } = new();
        public List<string> RestrictedFactions { get; init; } = new();
        public List<string> RestrictedSizes { get; init; } = new();
        public List<MechanicsHandler> Mechanics { get; init; } = new();
    }
    private sealed class MechanicsHandler
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string ReviewStatus { get; init; } = "";
        public string RuntimeStatus { get; init; } = "";
        public List<string> Evidence { get; init; } = new();
    }
}
