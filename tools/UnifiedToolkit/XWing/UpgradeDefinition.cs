using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public sealed class UpgradeDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";
    public string Title { get; init; } = "";
    public string Condition { get; init; } = "";

    public int Limited { get; init; }
    public int Charges { get; init; }
    public int Force { get; init; }

    public int HullModifier { get; init; }
    public int ShieldModifier { get; init; }
    public int EnergyModifier { get; init; }

    public bool Dual { get; init; }
    public bool Bomb { get; init; }
    public bool Docking { get; init; }
    public bool MoveThrough { get; init; }
    public bool WingLeader { get; init; }

    public List<string> AddedActions { get; } = new();
    public List<string> AddedSquadActions { get; } = new();
    public List<string> AddedSlots { get; } = new();
    public List<string> RemovedSlots { get; } = new();

    public LuaTableValue? Restrictions { get; init; }

    public required LuaEntity SourceEntity { get; init; }

    public required UpgradeRestrictions ParsedRestrictions { get; init; }
}