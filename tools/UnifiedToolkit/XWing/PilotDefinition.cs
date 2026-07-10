namespace UnifiedToolkit.XWing;

public sealed class PilotDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";

    public string Faction { get; init; } = "";
    public string ShipType { get; init; } = "";

    public int Initiative { get; init; }
    public int Limited { get; init; }
    public int Force { get; init; }
    public int Charges { get; init; }
    public int ShieldModifier { get; init; }

    public string Texture { get; init; } = "";

    public bool Docking { get; init; }

    public List<string> Actions { get; } = new();
    public List<string> Keywords { get; } = new();
    public List<string> AddedSlots { get; } = new();

    public ShipDefinition? Ship { get; internal set; }

    public bool IsLinkedToShip => Ship is not null;
}