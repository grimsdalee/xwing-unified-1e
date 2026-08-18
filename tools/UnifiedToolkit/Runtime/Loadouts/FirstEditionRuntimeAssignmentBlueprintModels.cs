namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionRuntimeAssignmentBlueprint
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Read-only runtime planning contract. No TTS object, Lua script, asset or gameplay state is modified.";
    public string ActivationPolicy { get; init; } = "All mechanics handlers are inactive until separately reviewed and implemented.";
    public FirstEditionRuntimeOwnerContract Owner { get; init; } = new();
    public List<FirstEditionRuntimeSlotContract> Slots { get; init; } = new();
    public List<FirstEditionRuntimeUpgradeContract> Upgrades { get; init; } = new();
    public FirstEditionRuntimeAssignmentCost Cost { get; init; } = new();
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public List<FirstEditionLoadoutIssue> LoadoutIssues { get; init; } = new();
    public bool SourceLoadoutValid { get; init; }
    public bool IsValid => SourceLoadoutValid && AcceptanceChecks.All(check => check.Passed);
}

public sealed class FirstEditionRuntimeOwnerContract
{
    public string PilotId { get; init; } = "";
    public string PilotImportId { get; init; } = "";
    public string PilotName { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string Faction { get; init; } = "";
    public string BaseSize { get; init; } = "";
    public string StablePilotKey { get; init; } = "";
    public string StableShipKey { get; init; } = "";
    public string BindingPhase { get; init; } = "post-ship-spawn";
    public string? ShipGuid { get; init; }
    public string? PilotCardGuid { get; init; }
    public string? DialGuid { get; init; }
}

public sealed class FirstEditionRuntimeSlotContract
{
    public string SlotId { get; init; } = "";
    public string Type { get; init; } = "";
    public int Ordinal { get; init; }
    public string Source { get; init; } = "";
    public string? AssignedUpgradeXws { get; init; }
}

public sealed class FirstEditionRuntimeUpgradeContract
{
    public int RequestIndex { get; init; }
    public string UpgradeId { get; init; } = "";
    public string Xws { get; init; } = "";
    public string Name { get; init; } = "";
    public string SlotId { get; init; } = "";
    public string SlotType { get; init; } = "";
    public int Points { get; init; }
    public bool Unique { get; init; }
    public bool Limited { get; init; }
    public string FaceRepositoryPath { get; init; } = "";
    public string BackRepositoryPath { get; init; } = "";
    public string EffectText { get; init; } = "";
    public string RuntimePriority { get; init; } = "";
    public string RuntimePriorityReason { get; init; } = "";
    public string StablePilotKey { get; init; } = "";
    public string StableShipKey { get; init; } = "";
    public string ActivationStatus { get; init; } = "inactive";
    public string BindingPhase { get; init; } = "post-ship-spawn";
    public string? UpgradeCardGuid { get; init; }
    public List<string> RestrictedShips { get; init; } = new();
    public List<string> RestrictedFactions { get; init; } = new();
    public List<string> RestrictedSizes { get; init; } = new();
    public List<FirstEditionRuntimeHandlerContract> Handlers { get; init; } = new();
    public List<FirstEditionRuntimeStateRequirement> StateRequirements { get; init; } = new();
    public List<FirstEditionRuntimeDependency> Dependencies { get; init; } = new();
}

public sealed class FirstEditionRuntimeHandlerContract
{
    public string HandlerId { get; init; } = "";
    public string MechanicId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ReviewStatus { get; init; } = "";
    public string CatalogueRuntimeStatus { get; init; } = "";
    public string ActivationStatus { get; init; } = "inactive";
    public List<string> Evidence { get; init; } = new();
}

public sealed class FirstEditionRuntimeStateRequirement
{
    public string Id { get; init; } = "";
    public string SourceMechanicId { get; init; } = "";
    public string Status { get; init; } = "contract-only";
}

public sealed class FirstEditionRuntimeDependency
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string FaceRepositoryPath { get; init; } = "";
    public string BackRepositoryPath { get; init; } = "";
    public string TokenRepositoryPath { get; init; } = "";
    public string BindingStatus { get; init; } = "unbound";
    public string? ObjectGuid { get; init; }
}

public sealed class FirstEditionRuntimeAssignmentCost
{
    public int Pilot { get; init; }
    public int Upgrades { get; init; }
    public int Total { get; init; }
}

public sealed class FirstEditionRuntimeAcceptanceCheck
{
    public string Id { get; init; } = "";
    public bool Passed { get; init; }
    public string Message { get; init; } = "";
}
