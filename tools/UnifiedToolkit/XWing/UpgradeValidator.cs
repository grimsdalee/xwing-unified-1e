namespace UnifiedToolkit.XWing;

public static class UpgradeValidator
{
    private static readonly HashSet<string> RecognizedFields =
        new(StringComparer.Ordinal)
        {
            "name",
            "slot",
            "restriction",
            "limited",
            "charge",
            "add_action",
            "remove_slots",
            "add_slots",
            "dual",
            "force",
            "card_back",
            "arcs",
            "shd",
            "bomb",
            "condition",
            "hull",
            "energy",
            "execute_options",
            "add_squad_action",
            "remotes",
            "wingleader",
            "point_modifier",
            "remote_charge",
            "title",
            "docking",
            "movethrough",
            "shield",
            "loadout_modifier"
        };

    public static List<UpgradeValidationIssue> Validate(
        IEnumerable<UpgradeDefinition> upgrades)
    {
        ArgumentNullException.ThrowIfNull(upgrades);

        var upgradeList = upgrades.ToList();
        var issues = new List<UpgradeValidationIssue>();

        ValidateDuplicateIds(upgradeList, issues);

        foreach (var upgrade in upgradeList)
        {
            ValidateRequiredFields(upgrade, issues);
            ValidateSourceFields(upgrade, issues);
        }

        return issues
            .OrderBy(issue => SeverityOrder(issue.Severity))
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.Slot)
            .ThenBy(issue => issue.UpgradeName)
            .ThenBy(issue => issue.UpgradeId)
            .ThenBy(issue => issue.FieldName)
            .ToList();
    }

    private static void ValidateDuplicateIds(
        IReadOnlyCollection<UpgradeDefinition> upgrades,
        ICollection<UpgradeValidationIssue> issues)
    {
        var duplicateGroups = upgrades
            .Where(upgrade =>
                !string.IsNullOrWhiteSpace(upgrade.Id))
            .GroupBy(
                upgrade => upgrade.Id,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            foreach (var upgrade in group)
            {
                issues.Add(CreateIssue(
                    upgrade,
                    severity: "Error",
                    code: "DuplicateUpgradeId",
                    message:
                        $"Upgrade ID '{upgrade.Id}' occurs " +
                        $"{group.Count()} times."));
            }
        }
    }

    private static void ValidateRequiredFields(
        UpgradeDefinition upgrade,
        ICollection<UpgradeValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(upgrade.Id))
        {
            issues.Add(CreateIssue(
                upgrade,
                severity: "Error",
                code: "MissingUpgradeId",
                message: "Upgrade has no database ID."));
        }

        if (string.IsNullOrWhiteSpace(upgrade.Name))
        {
            issues.Add(CreateIssue(
                upgrade,
                severity: "Error",
                code: "MissingUpgradeName",
                message:
                    $"Upgrade '{upgrade.Id}' has no name."));
        }

        if (string.IsNullOrWhiteSpace(upgrade.Slot))
        {
            issues.Add(CreateIssue(
                upgrade,
                severity: "Error",
                code: "MissingUpgradeSlot",
                message:
                    $"Upgrade '{upgrade.Id}' has no slot."));
        }
    }

    private static void ValidateSourceFields(
        UpgradeDefinition upgrade,
        ICollection<UpgradeValidationIssue> issues)
    {
        foreach (var field in upgrade.SourceEntity.Fields)
        {
            if (RecognizedFields.Contains(field.Key))
                continue;

            issues.Add(CreateIssue(
                upgrade,
                severity: "Warning",
                code: "UnknownUpgradeField",
                fieldName: field.Key,
                message:
                    $"Upgrade contains unrecognised source field " +
                    $"'{field.Key}'."));
        }
    }

    private static UpgradeValidationIssue CreateIssue(
        UpgradeDefinition upgrade,
        string severity,
        string code,
        string message,
        string fieldName = "")
    {
        return new UpgradeValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            UpgradeId = upgrade.Id,
            UpgradeName = upgrade.Name,
            Slot = upgrade.Slot,
            FieldName = fieldName
        };
    }

    private static int SeverityOrder(string severity)
    {
        return severity switch
        {
            "Error" => 0,
            "Warning" => 1,
            _ => 2
        };
    }
}