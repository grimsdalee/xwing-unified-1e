using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public static class UpgradeMapper
{
    public static UpgradeDefinition Map(LuaEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var restrictionResult =
            entity.ReadTable("restriction");

        var upgrade = new UpgradeDefinition
        {
            Id = entity.Id,

            Name = entity.ReadString("name")
                .ValueOrDefault(""),

            Slot = entity.ReadString("slot")
                .ValueOrDefault(""),

            Title = entity.ReadString("title")
                .ValueOrDefault(""),

            Condition = entity.ReadString("condition")
                .ValueOrDefault(""),

            Limited = entity.ReadInt("limited")
                .ValueOrDefault(0),

            Charges = entity.ReadInt("charge")
                .ValueOrDefault(0),

            Force = entity.ReadInt("force")
                .ValueOrDefault(0),

            HullModifier = entity.ReadInt("hull")
                .ValueOrDefault(0),

            ShieldModifier =
                ReadShieldModifier(entity),

            EnergyModifier = entity.ReadInt("energy")
                .ValueOrDefault(0),

            Dual = entity.ReadBool("dual")
                .ValueOrDefault(false),

            Bomb = entity.ReadBool("bomb")
                .ValueOrDefault(false),

            Docking = entity.ReadBool("docking")
                .ValueOrDefault(false),

            MoveThrough = entity.ReadBool("movethrough")
                .ValueOrDefault(false),

            WingLeader = entity.ReadBool("wingleader")
                .ValueOrDefault(false),

            Restrictions = restrictionResult.IsSuccess
                ? restrictionResult.Value
                : null,

            ParsedRestrictions =
                UpgradeRestrictionMapper.Map(
                    restrictionResult.IsSuccess
                        ? restrictionResult.Value
                        : null),

            SourceEntity = entity
        };

        upgrade.AddedActions.AddRange(
            entity.ReadStringList("add_action")
                .ValueOrDefault(Array.Empty<string>()));

        upgrade.AddedSquadActions.AddRange(
            entity.ReadStringList("add_squad_action")
                .ValueOrDefault(Array.Empty<string>()));

        upgrade.AddedSlots.AddRange(
            entity.ReadStringList("add_slots")
                .ValueOrDefault(Array.Empty<string>()));

        upgrade.RemovedSlots.AddRange(
            entity.ReadStringList("remove_slots")
                .ValueOrDefault(Array.Empty<string>()));

        return upgrade;
    }

    public static List<UpgradeDefinition> MapMany(
        IEnumerable<LuaEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        return entities
            .Select(Map)
            .OrderBy(upgrade => upgrade.Slot)
            .ThenBy(upgrade => upgrade.Name)
            .ThenBy(upgrade => upgrade.Id)
            .ToList();
    }

    private static int ReadShieldModifier(
        LuaEntity entity)
    {
        var shield = entity.ReadInt("shield");

        if (shield.IsSuccess)
            return shield.Value;

        return entity.ReadInt("shd")
            .ValueOrDefault(0);
    }
}