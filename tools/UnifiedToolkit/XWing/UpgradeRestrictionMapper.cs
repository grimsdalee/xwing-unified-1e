using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public static class UpgradeRestrictionMapper
{
    public static UpgradeRestrictions Map(
        LuaTableValue? table)
    {
        var restrictions = new UpgradeRestrictions
        {
            RequiresForce =
                table is not null &&
                ReadBoolean(table, "has_force"),

            RequiresLimitedPilot =
                table is not null &&
                ReadBoolean(table, "is_limited"),

            InitiativeGreaterThan =
                table is null
                    ? null
                    : ReadOptionalInt(
                        table,
                        "initiative_greater_than")
        };

        if (table is null)
            return restrictions;

        restrictions.Factions.AddRange(
            ReadEnabledKeys(table, "faction"));

        restrictions.Ships.AddRange(
            ReadEnabledKeys(table, "ship"));

        restrictions.Sizes.AddRange(
            ReadEnabledKeys(table, "size"));

        restrictions.Keywords.AddRange(
            ReadStringItems(table, "keywords"));

        restrictions.ShipKeywords.AddRange(
            ReadNestedStringItems(
                table,
                "ship",
                "keywords"));

        return restrictions;
    }

    private static IReadOnlyList<string> ReadEnabledKeys(
        LuaTableValue parent,
        string fieldName)
    {
        if (!parent.TryGetValue(
                fieldName,
                out var value) ||
            value is not LuaTableValue table)
        {
            return Array.Empty<string>();
        }

        return table.Fields
            .Where(field =>
                field.Value is LuaBooleanValue
                {
                    Value: true
                })
            .Select(field => field.Key)
            .ToList();
    }

    private static IReadOnlyList<string> ReadStringItems(
        LuaTableValue parent,
        string fieldName)
    {
        if (!parent.TryGetValue(
                fieldName,
                out var value) ||
            value is not LuaTableValue table)
        {
            return Array.Empty<string>();
        }

        return table.Items
            .OfType<LuaStringValue>()
            .Select(item => item.Value)
            .ToList();
    }

    private static IReadOnlyList<string>
        ReadNestedStringItems(
            LuaTableValue parent,
            string outerField,
            string innerField)
    {
        if (!parent.TryGetValue(
                outerField,
                out var outerValue) ||
            outerValue is not LuaTableValue outerTable)
        {
            return Array.Empty<string>();
        }

        return ReadStringItems(
            outerTable,
            innerField);
    }

    private static bool ReadBoolean(
        LuaTableValue table,
        string fieldName)
    {
        return table.TryGetValue(
                   fieldName,
                   out var value) &&
               value is LuaBooleanValue
               {
                   Value: true
               };
    }

    private static int? ReadOptionalInt(
        LuaTableValue table,
        string fieldName)
    {
        if (!table.TryGetValue(
                fieldName,
                out var value) ||
            value is not LuaNumberValue number)
        {
            return null;
        }

        if (number.Value < int.MinValue ||
            number.Value > int.MaxValue ||
            decimal.Truncate(number.Value) !=
            number.Value)
        {
            return null;
        }

        return decimal.ToInt32(number.Value);
    }
}