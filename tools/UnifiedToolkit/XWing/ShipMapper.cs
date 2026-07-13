using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public static class ShipMapper
{
    public static ShipDefinition Map(LuaEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var ship = new ShipDefinition
        {
            Id = entity.Id,

            Name = entity.ReadString("name")
                .ValueOrDefault(""),

            Size = entity.ReadString("size")
                .ValueOrDefault(""),

            Hull = entity.ReadInt("hull")
                .ValueOrDefault(0),

            Shield = entity.ReadInt("shield")
                .ValueOrDefault(0),

            Agility = entity.ReadInt("agility")
                .ValueOrDefault(0)
        };

        ship.Factions.AddRange(
            entity.ReadEnabledKeys("factions")
                .ValueOrDefault(Array.Empty<string>()));

        return ship;
    }

    public static List<ShipDefinition> MapMany(
        IEnumerable<LuaEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        return entities
            .Select(Map)
            .OrderBy(ship => ship.Name)
            .ThenBy(ship => ship.Id)
            .ToList();
    }
}