using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionRuntimeBindingManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string RuntimePolicy { get; init; } = "Ownership metadata and save/load persistence only; all mechanics handlers remain inactive.";
    public FirstEditionRuntimeOwnerBinding Owner { get; init; } = new();
    public List<FirstEditionRuntimeUpgradeBinding> Upgrades { get; init; } = new();
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(check => check.Passed);
}

public sealed class FirstEditionRuntimeOwnerBinding
{
    public string StablePilotKey { get; init; } = "";
    public string StableShipKey { get; init; } = "";
    public string PilotCardGuid { get; init; } = "";
    public string ShipGuid { get; init; } = "";
    public string DialGuid { get; init; } = "";
    public string ControllerGuid { get; init; } = "";
    public string BindingStatus { get; init; } = "bound";
}

public sealed class FirstEditionRuntimeUpgradeBinding
{
    public string UpgradeId { get; init; } = "";
    public string Xws { get; init; } = "";
    public string Name { get; init; } = "";
    public string SlotId { get; init; } = "";
    public string StablePilotKey { get; init; } = "";
    public string StableShipKey { get; init; } = "";
    public string UpgradeCardGuid { get; init; } = "";
    public string PilotCardGuid { get; init; } = "";
    public string ShipGuid { get; init; } = "";
    public string DialGuid { get; init; } = "";
    public string ControllerGuid { get; init; } = "";
    public string BindingStatus { get; init; } = "bound";
    public string ActivationStatus { get; init; } = "inactive";
    public List<FirstEditionRuntimeHandlerContract> Handlers { get; init; } = new();
}

public sealed class FirstEditionRuntimeBindingBuildResult
{
    public FirstEditionRuntimeAssignmentBlueprint Blueprint { get; init; } = new();
    public FirstEditionRuntimeBindingManifest Manifest { get; init; } = new();
    public JsonObject ValidationSave { get; init; } = new();
}
