namespace UnifiedToolkit.Runtime.Loadouts;

public enum FirstEditionLoadoutIssueSeverity { Info, Warning, Error }

public sealed class FirstEditionLoadoutPlan
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Policy { get; init; } = "Validation and planning only; no TTS or gameplay state is changed.";
    public FirstEditionLoadoutPilot Pilot { get; init; } = new();
    public FirstEditionLoadoutShip Ship { get; init; } = new();
    public List<FirstEditionLoadoutSlot> Slots { get; init; } = new();
    public List<FirstEditionUpgradeAssignment> Assignments { get; init; } = new();
    public List<FirstEditionLoadoutIssue> Issues { get; init; } = new();
    public int PilotCost { get; init; }
    public int UpgradeCost { get; init; }
    public int TotalCost { get; init; }
    public bool IsValid => Issues.All(issue => issue.Severity != FirstEditionLoadoutIssueSeverity.Error);
}

public sealed class FirstEditionLoadoutPilot
{
    public string Id { get; init; } = "";
    public string ImportId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ShipId { get; init; } = "";
    public string Faction { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public bool Unique { get; init; }
    public List<string> PrintedUpgradeSlots { get; init; } = new();
}

public sealed class FirstEditionLoadoutShip
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public List<string> Actions { get; init; } = new();
    public List<string> Factions { get; init; } = new();
}

public sealed class FirstEditionLoadoutSlot
{
    public string SlotId { get; init; } = "";
    public string Type { get; init; } = "";
    public int Ordinal { get; init; }
    public string Source { get; init; } = "printed";
    public string? AssignedUpgradeXws { get; set; }
}

public sealed class FirstEditionUpgradeAssignment
{
    public int RequestIndex { get; init; }
    public string Xws { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";
    public string? AssignedSlotId { get; set; }
    public int Points { get; init; }
    public bool Unique { get; init; }
    public bool Limited { get; init; }
    public string FaceRepositoryPath { get; init; } = "";
    public string BackRepositoryPath { get; init; } = "";
    public List<FirstEditionConditionLink> Conditions { get; init; } = new();
    public List<FirstEditionRuntimeCapability> RuntimeCapabilities { get; init; } = new();
    public bool RequiresStructuralReview { get; init; }
    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssignedSlotId);
}

public sealed class FirstEditionConditionLink
{
    public string Xws { get; init; } = "";
    public string Name { get; init; } = "";
    public string FaceRepositoryPath { get; init; } = "";
    public string BackRepositoryPath { get; init; } = "";
    public string TokenRepositoryPath { get; init; } = "";
}

public sealed class FirstEditionRuntimeCapability
{
    public string MechanicId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ReviewStatus { get; init; } = "";
    public string RuntimeStatus { get; init; } = "";
}

public sealed class FirstEditionLoadoutIssue
{
    public FirstEditionLoadoutIssueSeverity Severity { get; init; }
    public string Code { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class FirstEditionLoadoutRequest
{
    public string Pilot { get; init; } = "";
    public string? Ship { get; init; }
    public string? Faction { get; init; }
    public List<string> Upgrades { get; init; } = new();
}

public sealed class FirstEditionLoadoutContractVerification
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public int PilotCount { get; init; }
    public int ShipCount { get; init; }
    public int UpgradeCount { get; init; }
    public int MechanicsUpgradeCount { get; init; }
    public int ConditionAssignmentCount { get; init; }
    public int PrintedSlotCount { get; init; }
    public int DistinctSlotTypeCount { get; init; }
    public int AcceptanceScenarioCount { get; init; }
    public int AcceptanceScenarioFailureCount { get; init; }
    public List<FirstEditionLoadoutIssue> Issues { get; init; } = new();
    public bool IsValid => Issues.All(issue => issue.Severity != FirstEditionLoadoutIssueSeverity.Error);
}
