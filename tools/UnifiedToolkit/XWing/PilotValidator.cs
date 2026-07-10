namespace UnifiedToolkit.XWing;

public static class PilotValidator
{
    public static List<PilotValidationIssue> Validate(
        IEnumerable<PilotDefinition> pilots)
    {
        ArgumentNullException.ThrowIfNull(pilots);

        var pilotList = pilots.ToList();
        var issues = new List<PilotValidationIssue>();

        ValidateDuplicateIds(pilotList, issues);

        foreach (var pilot in pilotList)
        {
            ValidateRequiredFields(pilot, issues);
            ValidateShipLink(pilot, issues);
            ValidateFactionCompatibility(pilot, issues);
        }

        return issues
            .OrderBy(issue => SeverityOrder(issue.Severity))
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.Faction)
            .ThenBy(issue => issue.ShipType)
            .ThenBy(issue => issue.PilotName)
            .ThenBy(issue => issue.PilotId)
            .ToList();
    }

    private static void ValidateDuplicateIds(
        IReadOnlyCollection<PilotDefinition> pilots,
        ICollection<PilotValidationIssue> issues)
    {
        var duplicateGroups = pilots
            .Where(pilot => !string.IsNullOrWhiteSpace(pilot.Id))
            .GroupBy(
                pilot => pilot.Id,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            foreach (var pilot in group)
            {
                issues.Add(CreateIssue(
                    pilot,
                    severity: "Error",
                    code: "DuplicatePilotId",
                    message:
                        $"Pilot ID '{pilot.Id}' occurs " +
                        $"{group.Count()} times."));
            }
        }
    }

    private static void ValidateRequiredFields(
        PilotDefinition pilot,
        ICollection<PilotValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(pilot.Id))
        {
            issues.Add(CreateIssue(
                pilot,
                severity: "Error",
                code: "MissingPilotId",
                message: "Pilot has no database ID."));
        }

        if (string.IsNullOrWhiteSpace(pilot.Name))
        {
            issues.Add(CreateIssue(
                pilot,
                severity: "Error",
                code: "MissingPilotName",
                message:
                    $"Pilot '{pilot.Id}' has no name."));
        }

        if (string.IsNullOrWhiteSpace(pilot.Faction))
        {
            issues.Add(CreateIssue(
                pilot,
                severity: "Error",
                code: "MissingPilotFaction",
                message:
                    $"Pilot '{pilot.Id}' has no faction."));
        }

        if (string.IsNullOrWhiteSpace(pilot.ShipType))
        {
            issues.Add(CreateIssue(
                pilot,
                severity: "Error",
                code: "MissingPilotShipType",
                message:
                    $"Pilot '{pilot.Id}' has no ship_type."));
        }
    }

    private static void ValidateShipLink(
        PilotDefinition pilot,
        ICollection<PilotValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(pilot.ShipType))
            return;

        if (pilot.IsLinkedToShip)
            return;

        issues.Add(CreateIssue(
            pilot,
            severity: "Error",
            code: "UnknownPilotShipType",
            message:
                $"Pilot references ship_type " +
                $"'{pilot.ShipType}', but no matching " +
                "ShipDb entry was found."));
    }

    private static void ValidateFactionCompatibility(
        PilotDefinition pilot,
        ICollection<PilotValidationIssue> issues)
    {
        if (pilot.Ship is null)
            return;

        if (string.IsNullOrWhiteSpace(pilot.Faction))
            return;

        var factionIsSupported = pilot.Ship.Factions.Contains(
            pilot.Faction,
            StringComparer.OrdinalIgnoreCase);

        if (factionIsSupported)
            return;

        var supportedFactions = pilot.Ship.Factions.Count > 0
            ? string.Join(", ", pilot.Ship.Factions)
            : "(none)";

        issues.Add(CreateIssue(
            pilot,
            severity: "Warning",
            code: "PilotFactionNotSupportedByShip",
            message:
                $"Pilot faction '{pilot.Faction}' is not " +
                $"listed for ship '{pilot.Ship.Name}'. " +
                $"Ship factions: {supportedFactions}."));
    }

    private static PilotValidationIssue CreateIssue(
        PilotDefinition pilot,
        string severity,
        string code,
        string message)
    {
        return new PilotValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,

            PilotId = pilot.Id,
            PilotName = pilot.Name,
            Faction = pilot.Faction,
            ShipType = pilot.ShipType,
            ShipName = pilot.Ship?.Name ?? ""
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