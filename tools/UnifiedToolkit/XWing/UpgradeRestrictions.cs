namespace UnifiedToolkit.XWing;

public sealed class UpgradeRestrictions
{
    public List<string> Factions { get; } = new();

    public List<string> Ships { get; } = new();

    public List<string> Sizes { get; } = new();

    public List<string> Keywords { get; } = new();

    public List<string> ShipKeywords { get; } = new();

    public bool RequiresForce { get; init; }

    public bool RequiresLimitedPilot { get; init; }

    public int? InitiativeGreaterThan { get; init; }

    public bool HasAny =>
        Factions.Count > 0 ||
        Ships.Count > 0 ||
        Sizes.Count > 0 ||
        Keywords.Count > 0 ||
        ShipKeywords.Count > 0 ||
        RequiresForce ||
        RequiresLimitedPilot ||
        InitiativeGreaterThan.HasValue;
}