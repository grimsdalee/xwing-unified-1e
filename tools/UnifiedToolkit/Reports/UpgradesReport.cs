using System.Text;
using UnifiedToolkit.Lua.Model;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Reports;

public static class UpgradesReport
{
    public static void Write(
        IEnumerable<UpgradeDefinition> upgrades,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(upgrades);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath);

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.AppendLine(
            "Id,Name,Slot,Title,Limited,Charges,Force," +
            "HullModifier,ShieldModifier,EnergyModifier," +
            "Dual,Bomb,Docking,MoveThrough,WingLeader," +
            "Condition,AddedActions,AddedSquadActions," +
            "AddedSlots,RemovedSlots,Restrictions," +
            "RestrictedFactions,RestrictedShips," +
            "RestrictedSizes,RequiredKeywords," +
            "RequiredShipKeywords,RequiresForce," +
            "RequiresLimitedPilot,InitiativeGreaterThan");

        foreach (var upgrade in upgrades)
        {
            builder.AppendLine(string.Join(",",
                Csv(upgrade.Id),
                Csv(upgrade.Name),
                Csv(upgrade.Slot),
                Csv(upgrade.Title),
                Csv(upgrade.Limited.ToString()),
                Csv(upgrade.Charges.ToString()),
                Csv(upgrade.Force.ToString()),
                Csv(upgrade.HullModifier.ToString()),
                Csv(upgrade.ShieldModifier.ToString()),
                Csv(upgrade.EnergyModifier.ToString()),
                Csv(upgrade.Dual.ToString()),
                Csv(upgrade.Bomb.ToString()),
                Csv(upgrade.Docking.ToString()),
                Csv(upgrade.MoveThrough.ToString()),
                Csv(upgrade.WingLeader.ToString()),
                Csv(upgrade.Condition),

                Csv(string.Join(
                    " | ",
                    upgrade.AddedActions)),

                Csv(string.Join(
                    " | ",
                    upgrade.AddedSquadActions)),

                Csv(string.Join(
                    " | ",
                    upgrade.AddedSlots)),

                Csv(string.Join(
                    " | ",
                    upgrade.RemovedSlots)),

                Csv(upgrade.Restrictions is null
                    ? ""
                    : LuaValueFormatter.Format(
                        upgrade.Restrictions)),

                Csv(string.Join(
                    " | ",
                    upgrade.ParsedRestrictions.Factions)),

                Csv(string.Join(
                    " | ",
                    upgrade.ParsedRestrictions.Ships)),

                Csv(string.Join(
                    " | ",
                    upgrade.ParsedRestrictions.Sizes)),

                Csv(string.Join(
                    " | ",
                    upgrade.ParsedRestrictions.Keywords)),

                Csv(string.Join(
                    " | ",
                    upgrade.ParsedRestrictions.ShipKeywords)),

                Csv(upgrade.ParsedRestrictions
                    .RequiresForce.ToString()),

                Csv(upgrade.ParsedRestrictions
                    .RequiresLimitedPilot.ToString()),

                Csv(upgrade.ParsedRestrictions
                    .InitiativeGreaterThan?
                    .ToString() ?? "")));
        }

        File.WriteAllText(
            outputPath,
            builder.ToString(),
            new UTF8Encoding(false));
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') ||
            value.Contains('"') ||
            value.Contains('\n') ||
            value.Contains('\r'))
        {
            value = $"\"{value}\"";
        }

        return value;
    }
}