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
            Name = entity.GetString("name"),
            Title = entity.GetString("title"),

            Faction = entity.GetString("faction"),
            ShipType = entity.GetString("ship_type"),

            Initiative = entity.GetInt("initiative"),
            Limited = entity.GetInt("limited"),
            Force = entity.GetInt("force"),
            Charges = entity.GetInt("charge"),
            ShieldModifier = entity.GetInt("shield"),

            Texture = entity.GetString("texture"),
            Docking = entity.GetBool("docking")
        };

        pilot.Actions.AddRange(
            entity.GetStringList("action_set"));

        pilot.Keywords.AddRange(
            entity.GetStringList("keywords"));

        pilot.AddedSlots.AddRange(
            entity.GetStringList("add_slots"));

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