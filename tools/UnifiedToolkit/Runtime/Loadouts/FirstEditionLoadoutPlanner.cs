using System.Text.Json;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionLoadoutPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, string> SlotAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["astromech"] = "Astromech", ["bomb"] = "Bomb", ["cannon"] = "Cannon",
        ["cargo"] = "Cargo", ["crew"] = "Crew", ["elite"] = "Elite", ["ept"] = "Elite",
        ["hardpoint"] = "Hardpoint", ["illicit"] = "Illicit", ["missile"] = "Missile",
        ["modification"] = "Modification", ["mod"] = "Modification",
        ["salvagedastromech"] = "Salvaged Astromech", ["salvageddroid"] = "Salvaged Astromech",
        ["system"] = "System", ["team"] = "Team", ["tech"] = "Tech", ["title"] = "Title",
        ["torpedo"] = "Torpedo", ["turret"] = "Turret"
    };

    public FirstEditionLoadoutPlan Plan(string repository, FirstEditionLoadoutRequest request)
    {
        var data = Load(repository);
        var issues = new List<FirstEditionLoadoutIssue>();
        var pilot = ResolvePilot(data.Pilots, request, issues);
        if (pilot is null) return EmptyPlan(issues);
        var ship = ResolveShip(data, pilot, issues);
        if (ship is null)
        {
            return EmptyPlan(issues, pilot);
        }

        var size = NormalizeSize(ship.Size, issues, ship.Name);
        var slots = BuildSlots(pilot, issues);
        var assignments = new List<FirstEditionUpgradeAssignment>();
        var requestedUpgrades = request.Upgrades.SelectMany(SplitUpgradeArgument).ToList();
        var appliedStructuralUpgrades = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (request.EnableImplementedStructuralEffects)
        {
            foreach (var requested in requestedUpgrades)
            {
                var cardMatches = data.UpgradeCards.Where(card =>
                    Key(card.Xws) == Key(requested) || Key(card.CanonicalId) == Key(requested) || Key(card.Name) == Key(requested)).ToList();
                if (cardMatches.Count != 1) continue;
                var card = cardMatches[0];
                var mechanics = data.Mechanics.SingleOrDefault(candidate => Key(candidate.Xws) == Key(card.Xws));
                if (mechanics?.Mechanics.Any(item => item.Id == "upgrade-slot-change") != true
                    || !FirstEditionUpgradeSlotChangeHandler.Supports(card.Xws)) continue;

                var result = FirstEditionUpgradeSlotChangeHandler.Apply(card.Xws, ToPilot(pilot), slots);
                if (!result.Applied)
                    Error(issues, result.ErrorCode, card.Xws, result.Message);
                else
                    appliedStructuralUpgrades.Add(card.Xws);
            }
        }

        for (var index = 0; index < requestedUpgrades.Count; index++)
        {
            var requested = requestedUpgrades[index];
            var cardMatches = data.UpgradeCards.Where(card =>
                Key(card.Xws) == Key(requested) || Key(card.CanonicalId) == Key(requested) || Key(card.Name) == Key(requested)).ToList();
            if (cardMatches.Count != 1)
            {
                Error(issues, cardMatches.Count == 0 ? "upgrade-not-found" : "upgrade-ambiguous", requested,
                    cardMatches.Count == 0 ? $"Upgrade '{requested}' was not found." : $"Upgrade '{requested}' matched {cardMatches.Count} cards.");
                continue;
            }

            var card = cardMatches[0];
            var mechanics = data.Mechanics.SingleOrDefault(candidate => Key(candidate.Xws) == Key(card.Xws));
            if (mechanics is null)
                Error(issues, "mechanics-entry-missing", card.Xws, $"{card.Name} has no upgrade-mechanics entry.");

            var normalizedSlot = NormalizeSlot(card.Slot);
            if (normalizedSlot is null)
                Error(issues, "unknown-upgrade-slot", card.Xws, $"Upgrade slot '{card.Slot}' is not a recognized First Edition slot.");

            ValidateRestrictions(card, mechanics, pilot, ship, size, issues);
            var conditionLinks = data.ConditionAssignments
                .Where(link => link.SourceType.Equals("upgrade", StringComparison.OrdinalIgnoreCase) && Key(link.SourceXws) == Key(card.Xws))
                .Select(link => new FirstEditionConditionLink
                {
                    Xws = link.ConditionXws, Name = link.ConditionName,
                    FaceRepositoryPath = link.ConditionFaceRepositoryPath,
                    BackRepositoryPath = link.ConditionBackRepositoryPath,
                    TokenRepositoryPath = link.ConditionTokenRepositoryPath
                }).OrderBy(link => link.Xws, StringComparer.OrdinalIgnoreCase).ToList();

            var assignment = new FirstEditionUpgradeAssignment
            {
                RequestIndex = index + 1, Xws = card.Xws, Name = card.Name,
                Slot = normalizedSlot ?? card.Slot, Points = card.Points,
                Unique = card.Unique, Limited = card.Limited || mechanics?.IsLimited == true || mechanics?.IsSquadLimited == true,
                FaceRepositoryPath = card.FaceRepositoryPath, BackRepositoryPath = card.BackRepositoryPath,
                Conditions = conditionLinks,
                RuntimeCapabilities = mechanics?.Mechanics.Select(item => new FirstEditionRuntimeCapability
                {
                    MechanicId = item.Id, Name = item.Name,
                    ReviewStatus = item.ReviewStatus, RuntimeStatus = item.RuntimeStatus
                }).OrderBy(item => item.MechanicId, StringComparer.OrdinalIgnoreCase).ToList() ?? new(),
                RequiresStructuralReview = mechanics?.Mechanics.Any(item => item.Id == "upgrade-slot-change") == true
            };

            if (normalizedSlot is not null)
            {
                var available = slots.FirstOrDefault(slot => slot.Type == normalizedSlot && slot.AssignedUpgradeXws is null);
                if (available is null)
                    Error(issues, "slot-capacity-exceeded", card.Xws, $"No unoccupied {normalizedSlot} slot is available for {card.Name}.");
                else
                {
                    available.AssignedUpgradeXws = card.Xws;
                    assignment.AssignedSlotId = available.SlotId;
                }
            }

            if (assignment.RequiresStructuralReview)
            {
                if (appliedStructuralUpgrades.Contains(card.Xws))
                    Info(issues, "structural-effect-applied", card.Xws,
                        $"{card.Name}'s reviewed upgrade-slot change was applied to this loadout plan.");
                else
                    Warning(issues, "structural-effect-pending", card.Xws,
                        $"{card.Name} changes upgrade slots. Its structural effect is recorded but is not applied by this request.");
            }
            foreach (var capability in assignment.RuntimeCapabilities.Where(item => item.RuntimeStatus != "implemented"))
                Info(issues, "runtime-mechanic-not-implemented", card.Xws,
                    $"{capability.MechanicId}: {capability.RuntimeStatus}; review={capability.ReviewStatus}.");
            assignments.Add(assignment);
        }

        foreach (var group in assignments.GroupBy(assignment => Key(assignment.Xws)))
        {
            if (group.Count() > 1 && group.Any(assignment => assignment.Unique || assignment.Limited))
                Error(issues, "limited-upgrade-duplicated", group.First().Xws,
                    $"{group.First().Name} is unique or limited and appears {group.Count()} times.");
        }

        var upgradeCost = assignments.Where(assignment => assignment.IsAssigned).Sum(assignment => assignment.Points);
        return new FirstEditionLoadoutPlan
        {
            Pilot = ToPilot(pilot),
            Ship = new FirstEditionLoadoutShip
            {
                Id = string.IsNullOrWhiteSpace(ship.TargetId) ? ship.SourceId : ship.TargetId, Name = ship.Name, Size = size,
                Actions = ship.Actions, Factions = ship.Factions
            },
            Slots = slots, Assignments = assignments, Issues = OrderIssues(issues),
            PilotCost = pilot.SquadPointCost, UpgradeCost = upgradeCost,
            TotalCost = pilot.SquadPointCost + upgradeCost
        };
    }

    public FirstEditionLoadoutContractVerification Verify(string repository)
    {
        var data = Load(repository);
        var issues = new List<FirstEditionLoadoutIssue>();
        foreach (var pilot in data.Pilots)
        {
            foreach (var slot in pilot.UpgradeSlots)
                if (NormalizeSlot(slot) is null) Error(issues, "unknown-pilot-slot", pilot.Identity, $"Unknown printed slot '{slot}'.");
            ResolveShip(data, pilot, issues);
        }
        foreach (var card in data.UpgradeCards)
        {
            if (NormalizeSlot(card.Slot) is null) Error(issues, "unknown-upgrade-slot", card.Xws, $"Unknown upgrade slot '{card.Slot}'.");
            if (!data.Mechanics.Any(mechanic => Key(mechanic.Xws) == Key(card.Xws)))
                Error(issues, "mechanics-entry-missing", card.Xws, "No mechanics catalogue entry.");
            if (!File.Exists(Path.Combine(repository, card.FaceRepositoryPath.Replace('/', Path.DirectorySeparatorChar))))
                Error(issues, "upgrade-face-missing", card.Xws, card.FaceRepositoryPath);
            if (!File.Exists(Path.Combine(repository, card.BackRepositoryPath.Replace('/', Path.DirectorySeparatorChar))))
                Error(issues, "upgrade-back-missing", card.Xws, card.BackRepositoryPath);
        }
        foreach (var ship in data.Ships)
            NormalizeSize(ship.Size, issues, ship.Name);
        foreach (var link in data.ConditionAssignments.Where(link => link.SourceType.Equals("upgrade", StringComparison.OrdinalIgnoreCase)))
            if (!data.UpgradeCards.Any(card => Key(card.Xws) == Key(link.SourceXws)))
                Error(issues, "condition-source-upgrade-missing", link.SourceXws, $"Condition source for {link.ConditionName} has no upgrade card.");

        var scenarioFailures = 0;
        void Scenario(string name, FirstEditionLoadoutRequest request, Func<FirstEditionLoadoutPlan, bool> assertion)
        {
            try
            {
                var plan = Plan(repository, request);
                if (assertion(plan)) return;
            }
            catch (Exception exception)
            {
                scenarioFailures++;
                Error(issues, "acceptance-scenario-exception", name, exception.Message);
                return;
            }
            scenarioFailures++;
            Error(issues, "acceptance-scenario-failed", name, $"Loadout contract scenario '{name}' did not produce the expected result.");
        }
        Scenario("printed-duplicate-and-modification-slots", new()
        {
            Pilot = "redsquadronpilot",
            Upgrades = new() { "protontorpedoes", "r2astromech", "integratedastromech" }
        }, plan => plan.IsValid && plan.Assignments.Count(assignment => assignment.IsAssigned) == 3 &&
            plan.Assignments.Any(assignment => assignment.AssignedSlotId == "modification:1"));
        Scenario("title-slot", new()
        {
            Pilot = "goldsquadronpilot", Upgrades = new() { "btla4ywing" }
        }, plan => plan.IsValid && plan.Assignments.Single().AssignedSlotId == "title:1");
        Scenario("slot-capacity-rejection", new()
        {
            Pilot = "redsquadronpilot", Upgrades = new() { "protontorpedoes", "protontorpedoes" }
        }, plan => !plan.IsValid && plan.Issues.Any(issue => issue.Code == "slot-capacity-exceeded"));
        Scenario("faction-restriction-rejection", new()
        {
            Pilot = "redsquadronpilot", Upgrades = new() { "emperorpalpatine" }
        }, plan => !plan.IsValid && plan.Issues.Any(issue => issue.Code == "faction-restriction"));
        Scenario("condition-link", new()
        {
            Pilot = "backdraft", Upgrades = new() { "harpoonmissiles" }
        }, plan => plan.IsValid && plan.Assignments.Single().Conditions.Any(condition => condition.Xws == "harpooned"));

        return new FirstEditionLoadoutContractVerification
        {
            PilotCount = data.Pilots.Count, ShipCount = data.Ships.Count,
            UpgradeCount = data.UpgradeCards.Count, MechanicsUpgradeCount = data.Mechanics.Count,
            ConditionAssignmentCount = data.ConditionAssignments.Count,
            PrintedSlotCount = data.Pilots.Sum(pilot => pilot.UpgradeSlots.Count),
            DistinctSlotTypeCount = data.Pilots.SelectMany(pilot => pilot.UpgradeSlots)
                .Select(NormalizeSlot).Where(slot => slot is not null).Distinct().Count(),
            AcceptanceScenarioCount = 5, AcceptanceScenarioFailureCount = scenarioFailures,
            Issues = OrderIssues(issues)
        };
    }

    private static List<FirstEditionLoadoutSlot> BuildSlots(PilotRecord pilot, List<FirstEditionLoadoutIssue> issues)
    {
        var slots = new List<FirstEditionLoadoutSlot>();
        foreach (var printed in pilot.UpgradeSlots)
        {
            var normalized = NormalizeSlot(printed);
            if (normalized is null) { Error(issues, "unknown-pilot-slot", pilot.Identity, $"Unknown printed slot '{printed}'."); continue; }
            AddSlot(slots, normalized, "printed");
        }
        AddSlot(slots, "Modification", "implicit-first-edition");
        AddSlot(slots, "Title", "implicit-first-edition");
        return slots;
    }

    private static void AddSlot(List<FirstEditionLoadoutSlot> slots, string type, string source)
    {
        var ordinal = slots.Count(slot => slot.Type == type) + 1;
        slots.Add(new FirstEditionLoadoutSlot { Type = type, Ordinal = ordinal, SlotId = $"{Slug(type)}:{ordinal}", Source = source });
    }

    private static PilotRecord? ResolvePilot(List<PilotRecord> pilots, FirstEditionLoadoutRequest request, List<FirstEditionLoadoutIssue> issues)
    {
        var matches = pilots.Where(pilot => Key(pilot.CanonicalId) == Key(request.Pilot) || Key(pilot.Identity) == Key(request.Pilot) || Key(pilot.Name) == Key(request.Pilot));
        if (!string.IsNullOrWhiteSpace(request.Ship)) matches = matches.Where(pilot => Key(pilot.ShipId) == Key(request.Ship));
        if (!string.IsNullOrWhiteSpace(request.Faction)) matches = matches.Where(pilot => Key(pilot.Faction) == Key(request.Faction));
        var list = matches.ToList();
        if (list.Count == 1) return list[0];
        Error(issues, list.Count == 0 ? "pilot-not-found" : "pilot-ambiguous", request.Pilot,
            list.Count == 0 ? $"Pilot '{request.Pilot}' was not found with the supplied filters." :
            $"Pilot '{request.Pilot}' matched {list.Count} records. Add --ship and/or --faction.");
        return null;
    }

    private static ShipRecord? ResolveShip(RepositoryData data, PilotRecord pilot, List<FirstEditionLoadoutIssue> issues)
    {
        var shipKey = Key(pilot.ShipId);
        var baseShipKey = shipKey.EndsWith("fore", StringComparison.Ordinal) ? shipKey[..^4]
            : shipKey.EndsWith("aft", StringComparison.Ordinal) ? shipKey[..^3]
            : shipKey;
        var matches = data.Ships.Where(candidate =>
            Key(candidate.SourceId) == shipKey || Key(candidate.TargetId) == shipKey ||
            Key(candidate.SourceId) == baseShipKey || Key(candidate.TargetId) == baseShipKey).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
        {
            Error(issues, "ship-ambiguous", pilot.ShipId, $"Pilot ship matched {matches.Count} First Edition ship definitions.");
            return null;
        }

        var folderMatch = data.ShipFolders.FirstOrDefault(entry => Key(entry.Key) == shipKey || Key(entry.Key) == baseShipKey);
        if (!string.IsNullOrWhiteSpace(folderMatch.Key))
        {
            Warning(issues, "ship-metadata-fallback", pilot.ShipId,
                "No semantic ship definition exists; base size was resolved from ship-folder-map and actions remain unavailable.");
            return new ShipRecord
            {
                SourceId = baseShipKey, TargetId = pilot.ShipId, Name = pilot.ShipId,
                Size = folderMatch.Value.BaseSize, Factions = new() { pilot.Faction }
            };
        }
        if (shipKey.EndsWith("fore", StringComparison.Ordinal) || shipKey.EndsWith("aft", StringComparison.Ordinal))
        {
            Warning(issues, "epic-section-metadata-fallback", pilot.ShipId,
                "The fore/aft Epic section has no standalone semantic ship definition; Epic size is enforced and actions remain unavailable.");
            return new ShipRecord
            {
                SourceId = baseShipKey, TargetId = pilot.ShipId, Name = pilot.ShipId,
                Size = "epic", Factions = new() { pilot.Faction }
            };
        }
        Error(issues, "ship-not-found", pilot.ShipId, $"No First Edition ship definition or folder-map entry exists for pilot '{pilot.Name}'.");
        return null;
    }

    private static void ValidateRestrictions(UpgradeCardRecord card, MechanicsRecord? mechanics, PilotRecord pilot,
        ShipRecord ship, string size, List<FirstEditionLoadoutIssue> issues)
    {
        var factions = new List<string>();
        if (!string.IsNullOrWhiteSpace(card.Faction)) factions.Add(card.Faction);
        if (mechanics is not null) factions.AddRange(mechanics.RestrictedFactions);
        if (factions.Count > 0 && !factions.Any(faction => Key(faction) == Key(pilot.Faction)))
            Error(issues, "faction-restriction", card.Xws, $"{card.Name} requires {string.Join(" or ", factions.Distinct())}; pilot faction is {pilot.Faction}.");
        if (mechanics?.RestrictedShips.Count > 0 && !mechanics.RestrictedShips.Any(value => Key(value) == Key(ship.SourceId) || Key(value) == Key(ship.Name)))
            Error(issues, "ship-restriction", card.Xws, $"{card.Name} cannot be equipped by {ship.Name}.");
        if (mechanics?.RestrictedSizes.Count > 0 && !mechanics.RestrictedSizes.Any(value => Key(value) == Key(size)))
            Error(issues, "size-restriction", card.Xws, $"{card.Name} is restricted to {string.Join(" or ", mechanics.RestrictedSizes)}; ship size is {size}.");
    }

    private static string NormalizeSize(string value, List<FirstEditionLoadoutIssue> issues, string subject)
    {
        var normalized = Key(value) switch { "small" => "small", "large" => "large", "epic" or "huge" => "epic", _ => "" };
        if (normalized.Length == 0)
            Error(issues, Key(value) == "medium" ? "medium-base-rejected" : "unknown-base-size", subject,
                $"Base size '{value}' is not allowed in First Edition. Only small, large and Epic are valid.");
        return normalized.Length == 0 ? value : normalized;
    }

    public static string? NormalizeSlot(string value) => SlotAliases.TryGetValue(Key(value), out var normalized) ? normalized : null;
    private static IEnumerable<string> SplitUpgradeArgument(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string Key(string? value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Slug(string value) => value.ToLowerInvariant().Replace(' ', '-');
    private static FirstEditionLoadoutPilot ToPilot(PilotRecord pilot) => new()
    {
        Id = pilot.CanonicalId, ImportId = pilot.Identity, Name = pilot.Name, ShipId = pilot.ShipId,
        Faction = pilot.Faction, PilotSkill = pilot.PilotSkill, SquadPointCost = pilot.SquadPointCost,
        Unique = pilot.Unique, PrintedUpgradeSlots = pilot.UpgradeSlots
    };
    private static FirstEditionLoadoutPlan EmptyPlan(List<FirstEditionLoadoutIssue> issues, PilotRecord? pilot = null) => new()
    {
        Pilot = pilot is null ? new() : ToPilot(pilot), Issues = OrderIssues(issues), PilotCost = pilot?.SquadPointCost ?? 0
    };
    private static List<FirstEditionLoadoutIssue> OrderIssues(IEnumerable<FirstEditionLoadoutIssue> issues) => issues
        .OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code).ThenBy(issue => issue.Subject).ToList();
    private static void Error(List<FirstEditionLoadoutIssue> issues, string code, string subject, string message) => issues.Add(new() { Severity = FirstEditionLoadoutIssueSeverity.Error, Code = code, Subject = subject, Message = message });
    private static void Warning(List<FirstEditionLoadoutIssue> issues, string code, string subject, string message) => issues.Add(new() { Severity = FirstEditionLoadoutIssueSeverity.Warning, Code = code, Subject = subject, Message = message });
    private static void Info(List<FirstEditionLoadoutIssue> issues, string code, string subject, string message) => issues.Add(new() { Severity = FirstEditionLoadoutIssueSeverity.Info, Code = code, Subject = subject, Message = message });

    private static RepositoryData Load(string repository)
    {
        T Read<T>(params string[] parts) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(new[] { repository }.Concat(parts).ToArray())), JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize {string.Join('/', parts)}.");
        var mappedPilots = Read<List<PilotRecord>>("tools", "UnifiedToolkit", "ConversionData", "first-edition", "pilots.json");
        var officialPilots = Read<List<PilotRecord>>("tools", "UnifiedToolkit", "ConversionData", "first-edition", "official-pilots.json");
        var pilots = mappedPilots.Concat(officialPilots).GroupBy(pilot =>
            $"{Key(pilot.Faction)}|{Key(pilot.ShipId)}|{Key(pilot.CanonicalId)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(pilot => pilot.Faction).ThenBy(pilot => pilot.ShipId).ThenBy(pilot => pilot.Name).ToList();
        return new RepositoryData
        {
            Pilots = pilots,
            Ships = Read<List<ShipRecord>>("tools", "UnifiedToolkit", "ConversionData", "first-edition", "ships.json"),
            UpgradeCards = Read<UpgradeCardsDocument>("assets", "source", "unified1e", "reference", "cards", "upgrade-cards.json").UpgradeCards,
            Mechanics = Read<MechanicsDocument>("assets", "source", "unified1e", "reference", "cards", "upgrade-mechanics.json").Upgrades,
            ConditionAssignments = Read<ConditionSourcesDocument>("assets", "source", "unified1e", "reference", "cards", "condition-sources.json").Assignments,
            ShipFolders = Read<Dictionary<string, ShipFolderRecord>>("tools", "UnifiedToolkit", "ConversionData", "first-edition", "ship-folder-map.json")
        };
    }

    private sealed class RepositoryData { public List<PilotRecord> Pilots { get; init; } = new(); public List<ShipRecord> Ships { get; init; } = new(); public List<UpgradeCardRecord> UpgradeCards { get; init; } = new(); public List<MechanicsRecord> Mechanics { get; init; } = new(); public List<ConditionAssignmentRecord> ConditionAssignments { get; init; } = new(); public Dictionary<string, ShipFolderRecord> ShipFolders { get; init; } = new(StringComparer.OrdinalIgnoreCase); }
    private sealed class PilotRecord { public string ImportId { get; init; } = ""; public string MappingId { get; init; } = ""; public string Identity => string.IsNullOrWhiteSpace(ImportId) ? MappingId : ImportId; public string Id { get; init; } = ""; public string SourceId { get; init; } = ""; public string TargetId { get; init; } = ""; public string CanonicalId => !string.IsNullOrWhiteSpace(Id) ? Id : !string.IsNullOrWhiteSpace(TargetId) ? TargetId : SourceId; public string Name { get; init; } = ""; public string ShipId { get; init; } = ""; public string Faction { get; init; } = ""; public int PilotSkill { get; init; } public int SquadPointCost { get; init; } public bool Unique { get; init; } public List<string> UpgradeSlots { get; init; } = new(); }
    private sealed class ShipRecord { public string SourceId { get; init; } = ""; public string TargetId { get; init; } = ""; public string Name { get; init; } = ""; public string Size { get; init; } = ""; public List<string> Actions { get; init; } = new(); public List<string> Factions { get; init; } = new(); }
    private sealed class ShipFolderRecord { public string Folder { get; init; } = ""; public string BaseSize { get; init; } = ""; }
    private sealed class UpgradeCardsDocument { public List<UpgradeCardRecord> UpgradeCards { get; init; } = new(); }
    private sealed class UpgradeCardRecord { public string CanonicalId { get; init; } = ""; public string Name { get; init; } = ""; public string Xws { get; init; } = ""; public string Slot { get; init; } = ""; public int Points { get; init; } public bool Unique { get; init; } public bool Limited { get; init; } public string Faction { get; init; } = ""; public string FaceRepositoryPath { get; init; } = ""; public string BackRepositoryPath { get; init; } = ""; }
    private sealed class MechanicsDocument { public List<MechanicsRecord> Upgrades { get; init; } = new(); }
    private sealed class MechanicsRecord { public string Xws { get; init; } = ""; public List<string> RestrictedShips { get; init; } = new(); public List<string> RestrictedFactions { get; init; } = new(); public List<string> RestrictedSizes { get; init; } = new(); public bool IsLimited { get; init; } public bool IsSquadLimited { get; init; } public List<MechanicRecord> Mechanics { get; init; } = new(); }
    private sealed class MechanicRecord { public string Id { get; init; } = ""; public string Name { get; init; } = ""; public string ReviewStatus { get; init; } = ""; public string RuntimeStatus { get; init; } = ""; }
    private sealed class ConditionSourcesDocument { public List<ConditionAssignmentRecord> Assignments { get; init; } = new(); }
    private sealed class ConditionAssignmentRecord { public string SourceType { get; init; } = ""; public string SourceXws { get; init; } = ""; public string ConditionName { get; init; } = ""; public string ConditionXws { get; init; } = ""; public string ConditionFaceRepositoryPath { get; init; } = ""; public string ConditionBackRepositoryPath { get; init; } = ""; public string ConditionTokenRepositoryPath { get; init; } = ""; }
}
