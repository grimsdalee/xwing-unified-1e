using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public static class UpgradeRestrictionFlattener
{
    public static List<UpgradeRestrictionEntry> Flatten(
        IEnumerable<UpgradeDefinition> upgrades)
    {
        ArgumentNullException.ThrowIfNull(upgrades);

        var results = new List<UpgradeRestrictionEntry>();

        foreach (var upgrade in upgrades)
        {
            if (upgrade.Restrictions is null)
                continue;

            FlattenTable(
                upgrade,
                upgrade.Restrictions,
                pathPrefix: "",
                results);
        }

        return results
            .OrderBy(entry => entry.Slot)
            .ThenBy(entry => entry.UpgradeName)
            .ThenBy(entry => entry.UpgradeId)
            .ThenBy(entry => entry.Path)
            .ThenBy(entry => entry.Value)
            .ToList();
    }

    private static void FlattenTable(
        UpgradeDefinition upgrade,
        LuaTableValue table,
        string pathPrefix,
        ICollection<UpgradeRestrictionEntry> results)
    {
        foreach (var field in table.Fields)
        {
            var path = CombinePath(
                pathPrefix,
                field.Key);

            FlattenValue(
                upgrade,
                field.Value,
                path,
                results);
        }

        for (var index = 0;
             index < table.Items.Count;
             index++)
        {
            var path = CombinePath(
                pathPrefix,
                $"[{index + 1}]");

            FlattenValue(
                upgrade,
                table.Items[index],
                path,
                results);
        }
    }

    private static void FlattenValue(
        UpgradeDefinition upgrade,
        LuaValue value,
        string path,
        ICollection<UpgradeRestrictionEntry> results)
    {
        if (value is LuaTableValue nestedTable)
        {
            if (nestedTable.Fields.Count == 0 &&
                nestedTable.Items.Count == 0)
            {
                results.Add(CreateEntry(
                    upgrade,
                    path,
                    nestedTable));
            }
            else
            {
                FlattenTable(
                    upgrade,
                    nestedTable,
                    path,
                    results);
            }

            return;
        }

        results.Add(CreateEntry(
            upgrade,
            path,
            value));
    }

    private static UpgradeRestrictionEntry CreateEntry(
        UpgradeDefinition upgrade,
        string path,
        LuaValue value)
    {
        return new UpgradeRestrictionEntry
        {
            UpgradeId = upgrade.Id,
            UpgradeName = upgrade.Name,
            Slot = upgrade.Slot,
            Path = path,
            ValueKind = value.Kind.ToString(),
            Value = LuaValueFormatter.Format(value)
        };
    }

    private static string CombinePath(
        string prefix,
        string segment)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return segment;

        if (segment.StartsWith('['))
            return prefix + segment;

        return prefix + "." + segment;
    }
}