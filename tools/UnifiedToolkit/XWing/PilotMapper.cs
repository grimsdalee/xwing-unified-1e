using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public static class PilotMapper
{
    public static PilotDefinition Map(LuaEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var pilot = new PilotDefinition
        {
            Id = entity.Id,

            Name = entity.ReadString("name")
                .ValueOrDefault(""),

            Title = entity.ReadString("title")
                .ValueOrDefault(""),

            Faction = entity.ReadString("faction")
                .ValueOrDefault(""),

            ShipType = entity.ReadString("ship_type")
                .ValueOrDefault(""),

            Initiative = entity.ReadInt("initiative")
                .ValueOrDefault(0),

            Limited = entity.ReadInt("limited")
                .ValueOrDefault(0),

            Force = entity.ReadInt("force")
                .ValueOrDefault(0),

            Charges = entity.ReadInt("charge")
                .ValueOrDefault(0),

            ShieldModifier = entity.ReadInt("shield")
                .ValueOrDefault(0),

            Texture = entity.ReadString("texture")
                .ValueOrDefault(""),

            Docking = entity.ReadBool("docking")
                .ValueOrDefault(false)
        };

        pilot.Actions.AddRange(
            entity.ReadStringList("action_set")
                .ValueOrDefault(Array.Empty<string>()));

        pilot.Keywords.AddRange(
            entity.ReadStringList("keywords")
                .ValueOrDefault(Array.Empty<string>()));

        pilot.AddedSlots.AddRange(
            entity.ReadStringList("add_slots")
                .ValueOrDefault(Array.Empty<string>()));

        return pilot;
    }

    public static List<PilotDefinition> MapMany(
        IEnumerable<LuaEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        return entities
            .Select(Map)
            .OrderBy(pilot => pilot.Faction)
            .ThenBy(pilot => pilot.ShipType)
            .ThenByDescending(pilot => pilot.Initiative)
            .ThenBy(pilot => pilot.Name)
            .ThenBy(pilot => pilot.Id)
            .ToList();
    }
}